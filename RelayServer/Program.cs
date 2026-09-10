using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.EntityFrameworkCore;
using Serilog;
using RemoteAdminTool.RelayServer;

// ---- Serilog structured logging ----
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
    .WriteTo.File(
        Path.Combine(AppContext.BaseDirectory, "logs", "relay-.log"),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 14)
    .CreateLogger();

// ---- Configuration (appsettings.json -> env vars -> defaults) ----
var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog();

var config = builder.Configuration;

var superAdminToken = config["Auth:SuperAdminToken"]
    ?? Environment.GetEnvironmentVariable("RAT_SUPER_ADMIN_TOKEN")
    ?? "super-admin-change-me";

var hostToken = config["Auth:HostToken"]
    ?? Environment.GetEnvironmentVariable("RAT_HOST_TOKEN")
    ?? "host-secret-change-me";

// Make host token available as env var so ConnectionHub can read it
Environment.SetEnvironmentVariable("Auth__HostToken", hostToken);

var listenPort = int.TryParse(
    config["RelayServer:Port"]
    ?? Environment.GetEnvironmentVariable("RAT_PORT")
    ?? Environment.GetEnvironmentVariable("PORT"),   // Render injects PORT automatically
    out var p) ? p : 5050;
var useTlsStr = config["RelayServer:TLS"] ?? Environment.GetEnvironmentVariable("RAT_TLS");
var useTls = useTlsStr != "false" && useTlsStr != "False";
var pfxPath = config["RelayServer:CertPath"]
    ?? Environment.GetEnvironmentVariable("RAT_CERT_PATH") ?? "";
var pfxPassword = config["RelayServer:CertPassword"]
    ?? Environment.GetEnvironmentVariable("RAT_CERT_PASSWORD") ?? "dev-password";

// ---- PostgreSQL / EF Core ----
// Render injects DATABASE_URL in URI format: postgres://user:pass@host:port/db
// Npgsql requires key-value format: Host=...;Username=...;Password=...
// The helper below converts between the two formats.
static string BuildNpgsqlConnectionString(string raw)
{
    if (!raw.StartsWith("postgres://") && !raw.StartsWith("postgresql://"))
        return raw; // already in key-value format (local dev)

    var uri = new Uri(raw.Replace("postgres://", "postgresql://"));
    var userInfo = uri.UserInfo.Split(':', 2);
    var username = Uri.UnescapeDataString(userInfo[0]);
    var password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : "";
    var database = uri.AbsolutePath.TrimStart('/');
    var port = uri.Port > 0 ? uri.Port : 5432;

    // SSL Mode=Prefer works for both Render internal URLs (no SSL needed)
    // and external URLs (SSL available). Avoids crash on internal network.
    return $"Host={uri.Host};Port={port};Database={database};Username={username};Password={password};SSL Mode=Prefer;Trust Server Certificate=true;";
}

var rawDbUrl = Environment.GetEnvironmentVariable("DATABASE_URL")
    ?? config.GetConnectionString("Postgres")
    ?? "Host=localhost;Port=5432;Database=remote_admin;Username=postgres;Password=;";

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

var hub = new ConnectionHub(superAdminToken, tenantStore);

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
