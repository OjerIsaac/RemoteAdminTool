using System.Net.Sockets;
using System.Text.Json;
using RemoteAdminTool.Shared;

namespace RemoteAdminTool.Viewer;

public sealed class ViewerClient : IAsyncDisposable
{
    private TcpClient? _tcpClient;
    private NetworkStream? _stream;
    private CancellationTokenSource? _cts;

    public event EventHandler<byte[]>? FrameReceived;
    public event EventHandler<string>? Disconnected;

    public async Task<HelloAckMessage> ConnectAsync(string host, int port, string authToken, CancellationToken ct = default)
    {
        _tcpClient = new TcpClient();
        await _tcpClient.ConnectAsync(host, port, ct);
        _stream = _tcpClient.GetStream();

        var hello = new HelloMessage("1.0.0", authToken);
        await MessageFraming.WriteFrameAsync(_stream, MessageType.Hello,
            JsonSerializer.SerializeToUtf8Bytes(hello), ct);

        var (type, payload) = await MessageFraming.ReadFrameAsync(_stream, ct);
        if (type != MessageType.HelloAck)
        {
            throw new InvalidDataException("Expected HelloAck from host.");
        }

        var ack = JsonSerializer.Deserialize<HelloAckMessage>(payload);
        if (ack.Accepted)
        {
            _cts = new CancellationTokenSource();
            _ = ReceiveLoopAsync(_cts.Token);
        }

        return ack;
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested && _stream is not null)
            {
                var (type, payload) = await MessageFraming.ReadFrameAsync(_stream, ct);
                if (type == MessageType.FrameData)
                {
                    FrameReceived?.Invoke(this, payload);
                }
                else if (type == MessageType.Disconnect)
                {
                    break;
                }
            }
            Disconnected?.Invoke(this, "Host closed the connection.");
        }
        catch (Exception ex)
        {
            Disconnected?.Invoke(this, ex.Message);
        }
    }

    public Task SendMouseMoveAsync(int x, int y) =>
        SendAsync(MessageType.MouseMove, new MouseMoveMessage(x, y));

    public Task SendMouseButtonAsync(MouseButtonKind button, MouseButtonAction action, int x, int y) =>
        SendAsync(MessageType.MouseButton, new MouseButtonMessage(button, action, x, y));

    public Task SendMouseWheelAsync(int deltaY, int x, int y) =>
        SendAsync(MessageType.MouseWheel, new MouseWheelMessage(deltaY, x, y));

    public Task SendKeyAsync(int virtualKeyCode, KeyAction action) =>
        SendAsync(MessageType.KeyEvent, new KeyEventMessage(virtualKeyCode, action));

    private async Task SendAsync<T>(MessageType type, T message)
    {
        if (_stream is null) return;
        await MessageFraming.WriteFrameAsync(_stream, type, JsonSerializer.SerializeToUtf8Bytes(message));
    }

    public async ValueTask DisposeAsync()
    {
        _cts?.Cancel();
        if (_stream is not null)
        {
            try { await MessageFraming.WriteFrameAsync(_stream, MessageType.Disconnect, Array.Empty<byte>()); }
            catch { /* best effort */ }
        }
        _stream?.Dispose();
        _tcpClient?.Dispose();
    }
}
