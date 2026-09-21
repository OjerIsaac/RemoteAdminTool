using RemoteAdminTool.Shared;

namespace RemoteAdminTool.RelayServer;

/// <summary>
/// Bidirectional relay between an admin and a host during an active session.
/// Forwards FrameData from host → admin, and input messages from admin → host.
/// This is a pure pass-through - no processing, just shuttling bytes.
/// </summary>
public sealed class SessionRelay
{
    private readonly ConnectedAdmin _admin;
    private readonly ConnectedHost _host;
    private CancellationTokenSource? _cts;

    public SessionRelay(ConnectedAdmin admin, ConnectedHost host)
    {
        _admin = admin;
        _host = host;
        _cts = new CancellationTokenSource();
    }

    /// <summary>
    /// Reads frames from the host and forwards them to the admin.
    /// Runs until the session is stopped or either side disconnects.
    /// This handles: FrameData (screen frames), Heartbeat, Disconnect.
    /// </summary>
    public async Task RunHostToAdminRelayAsync(CancellationToken externalCt)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(externalCt, _cts?.Token ?? CancellationToken.None);
        var ct = linked.Token;

        try
        {
            while (!ct.IsCancellationRequested)
            {
                var (type, payload) = await MessageFraming.ReadFrameAsync(_host.Stream, ct);

                switch (type)
                {
                    case MessageType.FrameData:
                        // Forward screen frame to admin
                        await MessageFraming.WriteFrameAsync(_admin.Stream, MessageType.FrameData, payload, ct);
                        break;

                    case MessageType.Heartbeat:
                        _host.LastHeartbeat = DateTime.UtcNow;
                        break;

                    case MessageType.Disconnect:
                        Console.WriteLine($"[Relay] Host {_host.MachineName} sent disconnect during session");
                        return;

                    default:
                        // Forward any other host messages to admin as-is
                        await MessageFraming.WriteFrameAsync(_admin.Stream, type, payload, ct);
                        break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Session was stopped - normal
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Relay] Host→Admin relay error: {ex.Message}");
        }
    }

    /// <summary>
    /// Called by the hub when the admin sends an input message that should
    /// be forwarded to the host (MouseMove, KeyEvent, etc.).
    /// </summary>
    public async Task ForwardToHostAsync(MessageType type, byte[] payload, CancellationToken ct)
    {
        try
        {
            await MessageFraming.WriteFrameAsync(_host.Stream, type, payload, ct);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Relay] Admin→Host forward error: {ex.Message}");
        }
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }
}
