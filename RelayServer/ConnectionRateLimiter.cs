using System.Collections.Concurrent;

namespace RemoteAdminTool.RelayServer;

/// <summary>
/// Simple in-memory rate limiter for WebSocket connection attempts.
/// - Allows up to MaxConnectionsPerMinute new connections per IP per minute.
/// - After MaxFailuresBeforeBan consecutive auth failures from the same IP,
///   that IP is banned for BanDurationMinutes minutes.
/// Thread-safe. No external dependencies.
/// </summary>
public sealed class ConnectionRateLimiter
{
    private const int MaxConnectionsPerMinute = 15;
    private const int MaxFailuresBeforeBan    = 5;
    private const int BanDurationMinutes      = 10;

    private record ConnectionWindow(int Count, DateTime WindowStart);
    private record BanEntry(DateTime BannedUntil);
    private record FailureEntry(int Count);

    private readonly ConcurrentDictionary<string, ConnectionWindow> _windows  = new();
    private readonly ConcurrentDictionary<string, BanEntry>         _banned   = new();
    private readonly ConcurrentDictionary<string, FailureEntry>     _failures = new();
    private readonly Timer _cleanupTimer;

    public ConnectionRateLimiter()
    {
        // Clean up old entries every 5 minutes
        _cleanupTimer = new Timer(_ => Cleanup(), null,
            TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
    }

    /// <summary>
    /// Returns true if this IP is allowed to make a new connection attempt.
    /// Call this BEFORE accepting the WebSocket.
    /// </summary>
    public bool IsAllowed(string ip)
    {
        // Check if IP is currently banned
        if (_banned.TryGetValue(ip, out var ban) && DateTime.UtcNow < ban.BannedUntil)
            return false;

        // Check connection rate
        var now = DateTime.UtcNow;
        var window = _windows.AddOrUpdate(ip,
            _ => new ConnectionWindow(1, now),
            (_, existing) =>
            {
                if ((now - existing.WindowStart).TotalMinutes >= 1)
                    return new ConnectionWindow(1, now);     // new window
                return existing with { Count = existing.Count + 1 };
            });

        return window.Count <= MaxConnectionsPerMinute;
    }

    /// <summary>
    /// Record an authentication failure for this IP.
    /// If threshold is reached, the IP is temporarily banned.
    /// Call this after a rejected HostRegister or AdminAuth.
    /// </summary>
    public void RecordFailure(string ip)
    {
        var entry = _failures.AddOrUpdate(ip,
            _ => new FailureEntry(1),
            (_, existing) => existing with { Count = existing.Count + 1 });

        if (entry.Count >= MaxFailuresBeforeBan)
        {
            _banned[ip] = new BanEntry(DateTime.UtcNow.AddMinutes(BanDurationMinutes));
            _failures.TryRemove(ip, out _);
            Console.WriteLine($"[RateLimit] IP {ip} banned for {BanDurationMinutes} min after {MaxFailuresBeforeBan} auth failures.");
        }
    }

    /// <summary>
    /// Clear failures for an IP after a successful auth (so legitimate slow users aren't penalised).
    /// </summary>
    public void RecordSuccess(string ip)
    {
        _failures.TryRemove(ip, out _);
    }

    private void Cleanup()
    {
        var now = DateTime.UtcNow;

        foreach (var (ip, ban) in _banned)
            if (now >= ban.BannedUntil) _banned.TryRemove(ip, out _);

        foreach (var (ip, win) in _windows)
            if ((now - win.WindowStart).TotalMinutes >= 2) _windows.TryRemove(ip, out _);
    }
}
