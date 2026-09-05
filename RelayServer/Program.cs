using Microsoft.AspNetCore.Server.Kestrel.Core;
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

// ---- Configuration ----
var adminToken = Environment.GetEnvironmentVariable("RAT_ADMIN_TOKEN") ?? "admin-secret-change-me";
var hostToken = Environment.GetEnvironmentVariable("RAT_HOST_TOKEN") ?? "host-secret-change-me";
var listenPort = int.TryParse(Environment.GetEnvironmentVariable("RAT_PORT"), out var p) ? p : 5050;
var useTls = Environment.GetEnvironmentVariable("RAT_TLS") != "false"; // TLS on by default
var pfxPath = Environment.GetEnvironmentVariable("RAT_CERT_PATH") ?? "";
var pfxPassword = Environment.GetEnvironmentVariable("RAT_CERT_PASSWORD") ?? "dev-password";

var hub = new ConnectionHub(adminToken, hostToken);

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog();

// Configure Kestrel with TLS
builder.WebHost.ConfigureKestrel(options =>
{
    if (useTls)
    {
        var cert = CertificateHelper.GetOrCreateCertificate(pfxPath, pfxPassword);

        // Save dev cert for reuse across restarts
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

Console.WriteLine($"=== RemoteAdminTool Relay Server ===");
Console.WriteLine($"TLS: {(useTls ? "ENABLED" : "DISABLED")}");
Console.WriteLine($"Listening on {httpScheme}://0.0.0.0:{listenPort}");
Console.WriteLine($"WebSocket endpoint: {scheme}://localhost:{listenPort}/relay");
Console.WriteLine($"Health check: {httpScheme}://localhost:{listenPort}/");
Console.WriteLine();
Console.WriteLine("Environment variables:");
Console.WriteLine("  RAT_ADMIN_TOKEN  — Admin auth token");
Console.WriteLine("  RAT_HOST_TOKEN   — Host registration token");
Console.WriteLine("  RAT_PORT         — Listen port (default: 5050)");
Console.WriteLine("  RAT_TLS          — Set to 'false' to disable TLS");
Console.WriteLine("  RAT_CERT_PATH    — Path to .pfx certificate file");
Console.WriteLine("  RAT_CERT_PASSWORD — Certificate password");
Console.WriteLine();

app.Run();
