using System.Net.WebSockets;
using RemoteAdminTool.Shared;

namespace RemoteAdminTool.RelayServer;

/// <summary>
/// Bidirectional relay between an admin and a host during an active session.
/// Forwards frames between host and admin with thread-safe write locks.
/// This is a pure pass-through - no processing, just shuttling bytes.
/// </summary>
public sealed class SessionRelay
{
    private readonly ConnectedAdmin _admin;
    private readonly ConnectedHost _host;
    private readonly SemaphoreSlim _adminWriteLock = new(1, 1);
    private readonly SemaphoreSlim _hostWriteLock = new(1, 1);
    private int _stopped;

    public SessionRelay(ConnectedAdmin admin, ConnectedHost host)
    {
        _admin = admin;
        _host = host;
    }

    /// <summary>
    /// Forwards a frame received from the host to the admin.
    /// </summary>
    public async Task ForwardToAdminAsync(MessageType type, byte[] payload, CancellationToken ct)
    {
        if (Volatile.Read(ref _stopped) == 1) return;

        try
        {
            await _adminWriteLock.WaitAsync(ct);
            try
            {
                if (_admin.WebSocket.State == WebSocketState.Open)
                {
                    await MessageFraming.WriteFrameAsync(_admin.Stream, type, payload, ct);
                }
            }
            finally
            {
                _adminWriteLock.Release();
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Console.WriteLine($"[Relay] Host->Admin forward error: {ex.Message}");
        }
    }

    /// <summary>
    /// Called by the hub when the admin sends an input or command message that should
    /// be forwarded to the host (MouseMove, KeyEvent, ShellInput, StatsRequest, etc.).
    /// </summary>
    public async Task ForwardToHostAsync(MessageType type, byte[] payload, CancellationToken ct)
    {
        if (Volatile.Read(ref _stopped) == 1) return;

        try
        {
            await _hostWriteLock.WaitAsync(ct);
            try
            {
                if (_host.WebSocket.State == WebSocketState.Open)
                {
                    await MessageFraming.WriteFrameAsync(_host.Stream, type, payload, ct);
                }
            }
            finally
            {
                _hostWriteLock.Release();
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Console.WriteLine($"[Relay] Admin->Host forward error: {ex.Message}");
        }
    }

    public void Stop()
    {
        if (Interlocked.Exchange(ref _stopped, 1) == 1) return;

        _host.ActiveSession = null;

        // Best effort notify host that session ended so it resets state (stops screen capture, stats, shell)
        _ = Task.Run(async () =>
        {
            try
            {
                await _hostWriteLock.WaitAsync();
                try
                {
                    if (_host.WebSocket.State == WebSocketState.Open)
                    {
                        await MessageFraming.WriteFrameAsync(_host.Stream, MessageType.DisconnectFromHost,
                            Array.Empty<byte>(), CancellationToken.None);
                    }
                }
                finally
                {
                    _hostWriteLock.Release();
                }
            }
            catch { /* best effort */ }
        });
    }
}
