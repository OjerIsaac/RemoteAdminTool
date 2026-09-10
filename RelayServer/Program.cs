using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.EntityFrameworkCore;
using Pomelo.EntityFrameworkCore.MySql.Infrastructure;
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

var listenPort = int.TryParse(config["RelayServer:Port"] ?? Environment.GetEnvironmentVariable("RAT_PORT"), out var p)
    ? p : 5050;
var useTlsStr = config["RelayServer:TLS"] ?? Environment.GetEnvironmentVariable("RAT_TLS");
var useTls = useTlsStr != "false" && useTlsStr != "False";
var pfxPath = config["RelayServer:CertPath"]
    ?? Environment.GetEnvironmentVariable("RAT_CERT_PATH") ?? "";
var pfxPassword = config["RelayServer:CertPassword"]
    ?? Environment.GetEnvironmentVariable("RAT_CERT_PASSWORD") ?? "dev-password";

// ---- MySQL / EF Core ----
var connectionString = config.GetConnectionString("MySql")
    ?? Environment.GetEnvironmentVariable("RAT_MYSQL_CONNECTION")
    ?? "server=localhost;port=3306;database=remote_admin;user=root;password=;";

builder.Services.AddDbContextFactory<RelayDbContext>(options =>
    options.UseMySql(connectionString,
        ServerVersion.AutoDetect(connectionString),
        mySqlOptions => mySqlOptions.EnableRetryOnFailure(5)));

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
Console.WriteLine("  RAT_TLS                 — Set to 'false' to disable TLS");
Console.WriteLine("  RAT_CERT_PATH           — Path to .pfx certificate file");
Console.WriteLine("  RAT_CERT_PASSWORD       — Certificate password");
Console.WriteLine("  RAT_MYSQL_CONNECTION    — MySQL connection string");
Console.WriteLine();

app.Run();
