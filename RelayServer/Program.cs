using DotNetEnv;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Serilog;
using RemoteAdminTool.RelayServer;

// ---- Load .env file for local development ----
// On Render, env vars are set in the dashboard and take priority.
Env.TraversePath().Load();

// ---- Serilog console logging (no local logs/ folder created) ----
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

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
    ?? Environment.GetEnvironmentVariable("PORT"), // Render injects PORT automatically
    out var p) ? p : 5050;

// ---- PostgreSQL / EF Core ----
static string BuildNpgsqlConnectionString(string raw)
{
    if (!raw.StartsWith("postgres://") && !raw.StartsWith("postgresql://"))
        return raw;

    var uri = new Uri(raw.Replace("postgres://", "postgresql://"));
    var userInfo = uri.UserInfo.Split(':', 2);
    var username = Uri.UnescapeDataString(userInfo[0]);
    var password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : "";
    var database = uri.AbsolutePath.TrimStart('/');
    var port = uri.Port > 0 ? uri.Port : 5432;
    return $"Host={uri.Host};Port={port};Database={database};Username={username};Password={password};SSL Mode=Prefer;Trust Server Certificate=true;";
}

var rawDbUrl = Environment.GetEnvironmentVariable("DATABASE_URL")
    ?? config.GetConnectionString("Postgres")
    ?? throw new InvalidOperationException("No database connection string found. Set ConnectionStrings__Postgres in .env or DATABASE_URL on Render.");

var connectionString = BuildNpgsqlConnectionString(rawDbUrl);

builder.Services.AddDbContextFactory<RelayDbContext>(options =>
    options.UseNpgsql(connectionString,
        npgsqlOptions => npgsqlOptions.EnableRetryOnFailure(5)));

// ---- Forwarded Headers (TLS is terminated at Render's edge) ----
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

builder.Services.AddSingleton<TenantStore>();
builder.Services.AddSingleton<ConnectionRateLimiter>();

// ---- Keep-alive: self-ping every 2 min to prevent Render free tier sleep ----
builder.Services.AddHttpClient("keepalive", client =>
{
    client.Timeout = TimeSpan.FromSeconds(20);
});
builder.Services.AddHostedService<KeepAliveService>();

builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(listenPort);
});

var app = builder.Build();

app.UseForwardedHeaders();

// ---- Initialize database + tenant store ----
var tenantStore = app.Services.GetRequiredService<TenantStore>();
await tenantStore.InitializeAsync();

var hub = new ConnectionHub(superAdminToken, tenantStore,
    app.Services.GetRequiredService<IDbContextFactory<RelayDbContext>>());
var rateLimiter = app.Services.GetRequiredService<ConnectionRateLimiter>();

app.UseWebSockets(new WebSocketOptions
{
    KeepAliveInterval = TimeSpan.FromSeconds(30)
});

// Health check endpoint
app.MapGet("/", () => Results.Ok(new
{
    status = "running",
    service = "RemoteAdminTool RelayServer",
    timestamp = DateTime.UtcNow
}));

// Auto-update version manifest
app.MapGet("/api/version", async (HttpContext context) =>
{
    var providedToken = context.Request.Headers["X-Host-Token"].FirstOrDefault()?.Trim() ?? "";
    var globalToken   = Environment.GetEnvironmentVariable("Auth__HostToken") ?? "";

    if (string.IsNullOrEmpty(providedToken))
        return Results.Unauthorized();

    static bool FixedMatch(string a, string b) =>
        !string.IsNullOrEmpty(a) && !string.IsNullOrEmpty(b) &&
        System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(a),
            System.Text.Encoding.UTF8.GetBytes(b));

    var isAuthorized = FixedMatch(providedToken, globalToken)
        || FixedMatch(providedToken, superAdminToken)
        || (await tenantStore.GetByHostTokenAsync(providedToken)) is not null
        || (await tenantStore.GetByAdminTokenAsync(providedToken)) is not null;

    if (!isAuthorized)
        return Results.Unauthorized();

    var hostVersion   = Environment.GetEnvironmentVariable("HOST_VERSION")        ?? "1.0.0";
    var viewerVersion = Environment.GetEnvironmentVariable("VIEWER_VERSION")       ?? "1.0.0";
    var hostUrl       = Environment.GetEnvironmentVariable("HOST_DOWNLOAD_URL")    ?? "";
    var viewerUrl     = Environment.GetEnvironmentVariable("VIEWER_DOWNLOAD_URL")  ?? "";
    var hostSha256    = Environment.GetEnvironmentVariable("HOST_EXE_SHA256")      ?? "";
    var viewerSha256  = Environment.GetEnvironmentVariable("VIEWER_EXE_SHA256")    ?? "";

    return Results.Ok(new
    {
        host         = hostVersion,
        viewer       = viewerVersion,
        hostUrl      = hostUrl,
        viewerUrl    = viewerUrl,
        hostSha256   = hostSha256,
        viewerSha256 = viewerSha256
    });
});

// WebSocket endpoint - all hosts and admins connect here
app.Map("/relay", async (HttpContext context) =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = 400;
        await context.Response.WriteAsync("WebSocket connections only.");
        return;
    }

    var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    if (!rateLimiter.IsAllowed(ip))
    {
        context.Response.StatusCode = 429;
        await context.Response.WriteAsync("Too many connection attempts. Try again later.");
        return;
    }

    var ws = await context.WebSockets.AcceptWebSocketAsync();
    Console.WriteLine($"[Server] New WebSocket connection from {ip}");

    try
    {
        await hub.HandleConnectionAsync(ws, ip, rateLimiter, context.RequestAborted);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[Server] Connection error: {ex.Message}");
    }
});

Console.WriteLine("=== RemoteAdminTool Relay Server (Multi-Tenant) ===");
Console.WriteLine($"Listening on http://0.0.0.0:{listenPort}");
Console.WriteLine($"WebSocket endpoint: ws://localhost:{listenPort}/relay");

app.Run();
