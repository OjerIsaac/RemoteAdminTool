using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using RemoteAdminTool.Shared;

namespace RemoteAdminTool.Host;

public sealed class HostServer
{
    private readonly int _port;
    private readonly string _authToken;
    private readonly ScreenCapture _capture = new();
    private readonly InputInjector _input = new();

    public event EventHandler<bool>? SessionStateChanged;

    public HostServer(int port, string authToken)
    {
        _port = port;
        _authToken = authToken;
    }

    public async Task StartAsync(CancellationToken ct = default)
    {
        var listener = new TcpListener(IPAddress.Any, _port);
        listener.Start();

        while (!ct.IsCancellationRequested)
        {
            // Single-session model: this is a personal tool, not a multi-user server.
            using var client = await listener.AcceptTcpClientAsync(ct);
            using var stream = client.GetStream();

            try
            {
                await HandleSessionAsync(stream, ct);
            }
            catch (Exception ex)
            {
                // In production, log this properly. For now, swallow so the listener keeps running.
                Console.Error.WriteLine($"Session ended: {ex.Message}");
            }
            finally
            {
                SessionStateChanged?.Invoke(this, false);
            }
        }
    }

    private async Task HandleSessionAsync(NetworkStream stream, CancellationToken ct)
    {
        var (type, payload) = await MessageFraming.ReadFrameAsync(stream, ct);
        if (type != MessageType.Hello)
        {
            throw new InvalidDataException("Expected Hello as first message.");
        }

        var hello = JsonSerializer.Deserialize<HelloMessage>(payload)
            ?? throw new InvalidDataException("Malformed Hello message.");

        var accepted = hello.AuthToken == _authToken;
        var (screenW, screenH) = _capture.GetScreenSize();

        var ack = new HelloAckMessage(accepted, accepted ? null : "Invalid auth token", screenW, screenH);
        await MessageFraming.WriteFrameAsync(stream, MessageType.HelloAck,
            JsonSerializer.SerializeToUtf8Bytes(ack), ct);

        if (!accepted)
        {
            return;
        }

        SessionStateChanged?.Invoke(this, true);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        // Two loops running concurrently: sending frames out, receiving input in.
        var sendTask = FrameSendLoopAsync(stream, cts.Token);
        var receiveTask = InputReceiveLoopAsync(stream, cts.Token);

        var finished = await Task.WhenAny(sendTask, receiveTask);
        cts.Cancel();

        try { await Task.WhenAll(sendTask, receiveTask); }
        catch { /* expected once cancelled */ }
    }

    private async Task FrameSendLoopAsync(NetworkStream stream, CancellationToken ct)
    {
        // NOTE: this is a naive fixed-interval JPEG-per-frame sender to start with.
        // Swap in a proper delta/keyframe codec once the basic pipeline works end to end.
        const int targetFps = 15;
        var frameInterval = TimeSpan.FromMilliseconds(1000.0 / targetFps);

        while (!ct.IsCancellationRequested)
        {
            var frameBytes = _capture.CaptureFrameAsJpeg(quality: 60);
            await MessageFraming.WriteFrameAsync(stream, MessageType.FrameData, frameBytes, ct);
            await Task.Delay(frameInterval, ct);
        }
    }

    private async Task InputReceiveLoopAsync(NetworkStream stream, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var (type, payload) = await MessageFraming.ReadFrameAsync(stream, ct);

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
                    break; // no-op, just keeps the connection alive

                case MessageType.Disconnect:
                    return;
            }
        }
    }
}
