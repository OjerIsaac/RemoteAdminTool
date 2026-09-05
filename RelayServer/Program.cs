using RemoteAdminTool.RelayServer;

// ---- Configuration ----
// In production, load these from appsettings.json or environment variables.
var adminToken = Environment.GetEnvironmentVariable("RAT_ADMIN_TOKEN") ?? "admin-secret-change-me";
var hostToken = Environment.GetEnvironmentVariable("RAT_HOST_TOKEN") ?? "host-secret-change-me";
var listenPort = int.TryParse(Environment.GetEnvironmentVariable("RAT_PORT"), out var p) ? p : 5050;

var hub = new ConnectionHub(adminToken, hostToken);

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls($"http://0.0.0.0:{listenPort}");

var app = builder.Build();

// Enable WebSocket support
app.UseWebSockets(new WebSocketOptions
{
    KeepAliveInterval = TimeSpan.FromSeconds(30)
});

// Health check endpoint (useful for monitoring when deployed to a VM)
app.MapGet("/", () => Results.Ok(new
{
    status = "running",
    service = "RemoteAdminTool RelayServer",
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

Console.WriteLine($"=== RemoteAdminTool Relay Server ===");
Console.WriteLine($"Listening on http://0.0.0.0:{listenPort}");
Console.WriteLine($"WebSocket endpoint: ws://localhost:{listenPort}/relay");
Console.WriteLine($"Health check: http://localhost:{listenPort}/");
Console.WriteLine();

app.Run();
