namespace RemoteAdminTool.RelayServer;

/// <summary>
/// Pings the relay's own health endpoint every 2 minutes to prevent
/// Render's free tier from spinning down the service due to inactivity.
/// Runs as a hosted background service alongside the relay.
/// </summary>
public sealed class KeepAliveService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(2);
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<KeepAliveService> _logger;
    private readonly string _selfUrl;

    public KeepAliveService(IHttpClientFactory httpClientFactory,
        ILogger<KeepAliveService> logger,
        IConfiguration config)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;

        // On Render the service URL is the RENDER_EXTERNAL_URL env var
        // Fall back to localhost for local dev (no-op since local doesn't sleep)
        var externalUrl = Environment.GetEnvironmentVariable("RENDER_EXTERNAL_URL")
            ?? config["KeepAlive:SelfUrl"]
            ?? "http://localhost:5050";

        _selfUrl = externalUrl.TrimEnd('/') + "/";
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[KeepAlive] Started. Will ping {Url} every {Minutes} minutes.",
            _selfUrl, Interval.TotalMinutes);

        // Wait a bit after startup before first ping
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var client = _httpClientFactory.CreateClient("keepalive");
                var response = await client.GetAsync(_selfUrl, stoppingToken);
                _logger.LogInformation("[KeepAlive] Ping {Status} ← {Url}",
                    (int)response.StatusCode, _selfUrl);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning("[KeepAlive] Ping failed: {Message}", ex.Message);
            }

            await Task.Delay(Interval, stoppingToken);
        }
    }
}
