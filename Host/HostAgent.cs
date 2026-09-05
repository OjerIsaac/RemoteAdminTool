using System.Net.WebSockets;
using System.Text.Json;
using RemoteAdminTool.Shared;

namespace RemoteAdminTool.Host;

/// <summary>
/// Host agent that connects OUTBOUND to the relay server and registers itself.
/// When the relay brokers a session (admin wants to view this machine), the agent
/// begins sending screen frames and receiving input events through the relay.
///
/// This replaces the old HostServer that listened on a port — outbound connections
/// solve NAT traversal since only the relay needs to be publicly reachable.
/// </summary>
public sealed class HostAgent
{
    private readonly string _relayUrl;
    private readonly string _machineId;
    private readonly string _machineName;
    private readonly string _authToken;
    private readonly ScreenCapture _capture = new();
    private readonly InputInjector _input = new();

    private ClientWebSocket? _ws;
    private WebSocketStreamAdapter? _stream;

    public event EventHandler<bool>? SessionStateChanged;
    public event EventHandler<string>? ConnectionStateChanged;

    public HostAgent(string relayUrl, string machineId, string machineName, string authToken)
    {
        _relayUrl = relayUrl;
        _machineId = machineId;
        _machineName = machineName;
        _authToken = authToken;
    }

    /// <summary>
    /// Main loop: connect to relay, register, wait for sessions, reconnect on failure.
    /// Runs for the lifetime of the application.
    /// </summary>
    public async Task RunAsync(CancellationToken ct = default)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                ConnectionStateChanged?.Invoke(this, "Connecting to relay...");
                await ConnectAndRunAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[HostAgent] Connection lost: {ex.Message}");
                ConnectionStateChanged?.Invoke(this, $"Disconnected: {ex.Message}");
                SessionStateChanged?.Invoke(this, false);
            }

            // Reconnect with backoff
            if (!ct.IsCancellationRequested)
            {
                ConnectionStateChanged?.Invoke(this, "Reconnecting in 5s...");
                await Task.Delay(TimeSpan.FromSeconds(5), ct);
            }
        }
    }

    private async Task ConnectAndRunAsync(CancellationToken ct)
    {
        _ws = new ClientWebSocket();
        await _ws.ConnectAsync(new Uri(_relayUrl), ct);
        _stream = new WebSocketStreamAdapter(_ws);

        ConnectionStateChanged?.Invoke(this, "Connected to relay");

        // Register with the relay
        var register = new HostRegisterMessage(
            _machineId,
            _machineName,
            Environment.OSVersion.VersionString,
            "1.0.0",
            _authToken
        );
        await MessageFraming.WriteFrameAsync(_stream, MessageType.HostRegister,
            JsonSerializer.SerializeToUtf8Bytes(register), ct);

        // Wait for ack
        var (ackType, ackPayload) = await MessageFraming.ReadFrameAsync(_stream, ct);
        if (ackType != MessageType.HostRegisterAck)
        {
            throw new InvalidDataException("Expected HostRegisterAck from relay");
        }

        var ack = JsonSerializer.Deserialize<HostRegisterAckMessage>(ackPayload);
        if (!ack.Accepted)
        {
            throw new InvalidOperationException($"Registration rejected: {ack.RejectReason}");
        }

        ConnectionStateChanged?.Invoke(this, $"Registered as {_machineName}");
        Console.WriteLine($"[HostAgent] Registered with relay as {_machineName} ({_machineId})");

        // Idle loop — wait for sessions from the relay
        await IdleLoopAsync(ct);
    }

    /// <summary>
    /// Waits for the relay to signal a session (admin wants to connect).
    /// When SessionStarted arrives, runs the frame-send + input-receive loops.
    /// </summary>
    private async Task IdleLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _ws!.State == WebSocketState.Open)
        {
            var (type, payload) = await MessageFraming.ReadFrameAsync(_stream!, ct);

            switch (type)
            {
                case MessageType.SessionStarted:
                    Console.WriteLine("[HostAgent] Session started — admin connected");
                    SessionStateChanged?.Invoke(this, true);
                    await RunSessionAsync(ct);
                    SessionStateChanged?.Invoke(this, false);
                    Console.WriteLine("[HostAgent] Session ended — back to idle");
                    break;

                case MessageType.Heartbeat:
                    // Reply with heartbeat
                    await MessageFraming.WriteFrameAsync(_stream!, MessageType.Heartbeat,
                        Array.Empty<byte>(), ct);
                    break;

                case MessageType.Disconnect:
                    return;
            }
        }
    }

    /// <summary>
    /// Active session: send screen frames to the relay (which forwards to admin),
    /// and receive input events from the relay (which came from admin).
    /// </summary>
    private async Task RunSessionAsync(CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        var sendTask = FrameSendLoopAsync(cts.Token);
        var receiveTask = InputReceiveLoopAsync(cts.Token);

        var finished = await Task.WhenAny(sendTask, receiveTask);
        cts.Cancel();

        try { await Task.WhenAll(sendTask, receiveTask); }
        catch { /* expected once cancelled */ }
    }

    private async Task FrameSendLoopAsync(CancellationToken ct)
    {
        // NOTE: this is a naive fixed-interval JPEG-per-frame sender to start with.
        // Swap in a proper delta/keyframe codec once the basic pipeline works end to end.
        const int targetFps = 15;
        var frameInterval = TimeSpan.FromMilliseconds(1000.0 / targetFps);

        while (!ct.IsCancellationRequested && _stream is not null)
        {
            var frameBytes = _capture.CaptureFrameAsJpeg(quality: 60);
            await MessageFraming.WriteFrameAsync(_stream, MessageType.FrameData, frameBytes, ct);
            await Task.Delay(frameInterval, ct);
        }
    }

    private async Task InputReceiveLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _stream is not null)
        {
            var (type, payload) = await MessageFraming.ReadFrameAsync(_stream, ct);

            switch (type)
            {
                case MessageType.MouseMove:
                    var move = JsonSerializer.Deserialize<MouseMoveMessage>(payload);
                    _input.MoveMouse(move.X, move.Y);
                    break;

                case MessageType.MouseButton:
                    var btn = JsonSerializer.Deserialize<MouseButtonMessage>(payload);
                    _input.ClickMouse(btn.Button, btn.Action, btn.X, btn.Y);
                    break;

                case MessageType.MouseWheel:
                    var wheel = JsonSerializer.Deserialize<MouseWheelMessage>(payload);
                    _input.ScrollMouse(wheel.DeltaY);
                    break;

                case MessageType.KeyEvent:
                    var key = JsonSerializer.Deserialize<KeyEventMessage>(payload);
                    _input.SendKey(key.VirtualKeyCode, key.Action);
                    break;

                case MessageType.Heartbeat:
                    break;

                case MessageType.Disconnect:
                case MessageType.DisconnectFromHost:
                    return; // Session ended by admin
            }
        }
    }
}
