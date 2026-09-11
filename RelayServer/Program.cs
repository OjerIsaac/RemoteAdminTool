using DotNetEnv;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.EntityFrameworkCore;
using Serilog;
using RemoteAdminTool.RelayServer;

// ---- Load .env file for local development ----
// On Render, env vars are set in the dashboard and take priority.
// Env.TraversePath() walks up from the binary to find .env at solution root.
Env.TraversePath().Load();

// ---- Serilog structured logging ----
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
    .WriteTo.File(
        Path.Combine(AppContext.BaseDirectory, "logs", "relay-.log"),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 14)
    .CreateLogger();

// ---- Configuration ----
// ASP.NET Core merges (in priority order):
//   1. Environment variables (Render dashboard / .env loaded above)
//   2. appsettings.json (placeholder values only — safe to commit)
// Env var naming: double-underscore maps to nested JSON keys
//   e.g. Auth__SuperAdminToken  →  config["Auth:SuperAdminToken"]

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog();

var config = builder.Configuration;

var superAdminToken = config["Auth:SuperAdminToken"]
    ?? throw new InvalidOperationException("Auth__SuperAdminToken is not set. Add it to .env or Render env vars.");

var hostToken = config["Auth:HostToken"]
    ?? throw new InvalidOperationException("Auth__HostToken is not set. Add it to .env or Render env vars.");

// Make host token available so ConnectionHub can read it
Environment.SetEnvironmentVariable("Auth__HostToken", hostToken);

var listenPort = int.TryParse(
    config["RelayServer:Port"]
    ?? Environment.GetEnvironmentVariable("PORT"),  // Render injects PORT automatically
    out var p) ? p : 5050;

var useTls = config["RelayServer:TLS"] is not ("false" or "False");
var pfxPath = config["RelayServer:CertPath"] ?? "";
var pfxPassword = config["RelayServer:CertPassword"] ?? "dev-password";

// ---- PostgreSQL / EF Core ----
// Render injects DATABASE_URL as a URI: postgres://user:pass@host:port/db
// Our .env uses ConnectionStrings__Postgres (same URI format via the key above)
// The helper converts either format to Npgsql key-value format.
static string BuildNpgsqlConnectionString(string raw)
{
    if (!raw.StartsWith("postgres://") && !raw.StartsWith("postgresql://"))
        return raw; // already key-value format

    var uri = new Uri(raw.Replace("postgres://", "postgresql://"));
    var userInfo = uri.UserInfo.Split(':', 2);
    var username = Uri.UnescapeDataString(userInfo[0]);
    var password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : "";
    var database = uri.AbsolutePath.TrimStart('/');
    var port = uri.Port > 0 ? uri.Port : 5432;
    return $"Host={uri.Host};Port={port};Database={database};Username={username};Password={password};SSL Mode=Prefer;Trust Server Certificate=true;";
}

// DATABASE_URL (Render) takes priority over ConnectionStrings:Postgres (.env / appsettings)
var rawDbUrl = Environment.GetEnvironmentVariable("DATABASE_URL")
    ?? config.GetConnectionString("Postgres")
    ?? throw new InvalidOperationException("No database connection string found. Set ConnectionStrings__Postgres in .env or DATABASE_URL on Render.");

var connectionString = BuildNpgsqlConnectionString(rawDbUrl);

builder.Services.AddDbContextFactory<RelayDbContext>(options =>
    options.UseNpgsql(connectionString,
        npgsqlOptions => npgsqlOptions.EnableRetryOnFailure(5)));

// ---- Forwarded Headers (required on Render — TLS is terminated at their edge) ----
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    // Trust all proxies (Render's infra) — restrict to specific IPs in high-security setups
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

builder.Services.AddSingleton<TenantStore>();

// ---- Keep-alive: self-ping every 2 min to prevent Render free tier sleep ----
builder.Services.AddHttpClient("keepalive", client =>
{
    client.Timeout = TimeSpan.FromSeconds(20);
});
builder.Services.AddHostedService<KeepAliveService>();

// Configure Kestrel with TLS
builder.WebHost.ConfigureKestrel(options =>
{
    if (useTls)
    {
        var cert = CertificateHelper.GetOrCreateCertificate(pfxPath, pfxPassword);

        var devCertPath = Path.Combine(AppContext.BaseDirectory, "dev-cert.pfx");
        if (string.IsNullOrEmpty(pfxPath) && !File.Exists(devCertPath))
        {
            CertificateHelper.SaveCertificate(cert, devCertPath, pfxPassword);
        }

        options.ListenAnyIP(listenPort, listenOptions =>
        {
            listenOptions.Protocols = HttpProtocols.Http1AndHttp2;
            listenOptions.UseHttps(cert);
        });
    }
    else
    {
        options.ListenAnyIP(listenPort);
    }
});

var app = builder.Build();

// Must be first middleware — lets Kestrel know the original scheme was WSS/HTTPS
app.UseForwardedHeaders();

// ---- Initialize database + tenant store ----
var tenantStore = app.Services.GetRequiredService<TenantStore>();
await tenantStore.InitializeAsync();

var hub = new ConnectionHub(superAdminToken, tenantStore,
    app.Services.GetRequiredService<IDbContextFactory<RelayDbContext>>());

// Enable WebSocket support
app.UseWebSockets(new WebSocketOptions
{
    KeepAliveInterval = TimeSpan.FromSeconds(30)
});

// Health check endpoint
app.MapGet("/", () => Results.Ok(new
{
    status = "running",
    service = "RemoteAdminTool RelayServer",
    tls = useTls,
    timestamp = DateTime.UtcNow
}));

// WebSocket endpoint — all hosts and admins connect here
app.Map("/relay", async (HttpContext context) =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = 400;
        await context.Response.WriteAsync("WebSocket connections only.");
        return;
    }

    var ws = await context.WebSockets.AcceptWebSocketAsync();
    Console.WriteLine($"[Server] New WebSocket connection from {context.Connection.RemoteIpAddress}");

    try
    {
        await hub.HandleConnectionAsync(ws, context.RequestAborted);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[Server] Connection error: {ex.Message}");
    }
});

var scheme = useTls ? "wss" : "ws";
var httpScheme = useTls ? "https" : "http";

Console.WriteLine("=== RemoteAdminTool Relay Server (Multi-Tenant) ===");
Console.WriteLine($"TLS: {(useTls ? "ENABLED" : "DISABLED")}");
Console.WriteLine($"Listening on {httpScheme}://0.0.0.0:{listenPort}");
Console.WriteLine($"WebSocket endpoint: {scheme}://localhost:{listenPort}/relay");
Console.WriteLine($"Health check: {httpScheme}://localhost:{listenPort}/");
Console.WriteLine();
Console.WriteLine("Environment variables:");
Console.WriteLine("  RAT_SUPER_ADMIN_TOKEN   — Super admin token (platform owner)");
Console.WriteLine("  RAT_HOST_TOKEN          — Host registration token");
Console.WriteLine("  RAT_PORT                — Listen port (default: 5050)");
Console.WriteLine("  RAT_TLS                 — Set to 'false' to disable TLS (always false on Render)");
Console.WriteLine("  RAT_CERT_PATH           — Path to .pfx certificate file");
Console.WriteLine("  RAT_CERT_PASSWORD       — Certificate password");
Console.WriteLine("  DATABASE_URL            — PostgreSQL connection string (set automatically by Render Postgres)");
Console.WriteLine();

app.Run();
