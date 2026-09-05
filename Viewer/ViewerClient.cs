using System.IO;
using System.Net.WebSockets;
using System.Text.Json;
using RemoteAdminTool.Shared;

namespace RemoteAdminTool.Viewer;

/// <summary>
/// Admin viewer client that connects to the relay server, authenticates as admin,
/// receives the list of online hosts, and can start sessions with any of them.
/// During a session, screen frames arrive from the relay and input events are sent to the relay.
/// </summary>
public sealed class ViewerClient : IAsyncDisposable
{
    private ClientWebSocket? _ws;
    private WebSocketStreamAdapter? _stream;
    private CancellationTokenSource? _cts;

    public event EventHandler<byte[]>? FrameReceived;
    public event EventHandler<HostInfoMessage[]>? HostListReceived;
    public event EventHandler<HostOnlineMessage>? HostCameOnline;
    public event EventHandler<HostOfflineMessage>? HostWentOffline;
    public event EventHandler<SessionStartedMessage>? SessionStarted;
    public event EventHandler<string>? ClipboardReceived;
    public event EventHandler<MonitorInfoData[]>? MonitorListReceived;
    public event EventHandler<ChatMessageData>? ChatReceived;
    public event EventHandler<string>? Disconnected;

    /// <summary>
    /// Connect to the relay server and authenticate as admin.
    /// Returns true if authenticated successfully.
    /// </summary>
    public async Task<(bool Accepted, string? RejectReason)> ConnectAsync(
        string relayUrl, string adminToken, CancellationToken ct = default)
    {
        _ws = new ClientWebSocket();
        await _ws.ConnectAsync(new Uri(relayUrl), ct);
        _stream = new WebSocketStreamAdapter(_ws);

        // Authenticate as admin
        var auth = new AdminAuthMessage(adminToken);
        await MessageFraming.WriteFrameAsync(_stream, MessageType.AdminAuth,
            JsonSerializer.SerializeToUtf8Bytes(auth), ct);

        // Wait for auth ack
        var (type, payload) = await MessageFraming.ReadFrameAsync(_stream, ct);
        if (type != MessageType.AdminAuthAck)
        {
            throw new InvalidDataException("Expected AdminAuthAck from relay.");
        }

        var ack = JsonSerializer.Deserialize<AdminAuthAckMessage>(payload);
        if (ack.Accepted)
        {
            _cts = new CancellationTokenSource();
            _ = ReceiveLoopAsync(_cts.Token);
        }

        return (ack.Accepted, ack.RejectReason);
    }

    /// <summary>
    /// Request the relay to start a session with a specific host.
    /// </summary>
    public async Task ConnectToHostAsync(string machineId)
    {
        if (_stream is null) return;
        var msg = new ConnectToHostMessage(machineId);
        await MessageFraming.WriteFrameAsync(_stream, MessageType.ConnectToHost,
            JsonSerializer.SerializeToUtf8Bytes(msg));
    }

    /// <summary>
    /// Tell the relay to end the current session.
    /// </summary>
    public async Task DisconnectFromHostAsync(string machineId)
    {
        if (_stream is null) return;
        var msg = new DisconnectFromHostMessage(machineId);
        await MessageFraming.WriteFrameAsync(_stream, MessageType.DisconnectFromHost,
            JsonSerializer.SerializeToUtf8Bytes(msg));
    }

    /// <summary>
    /// Request an updated host list from the relay.
    /// </summary>
    public async Task RequestHostListAsync()
    {
        if (_stream is null) return;
        await MessageFraming.WriteFrameAsync(_stream, MessageType.RequestHostList,
            Array.Empty<byte>());
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested && _stream is not null)
            {
                var (type, payload) = await MessageFraming.ReadFrameAsync(_stream, ct);

                switch (type)
                {
                    case MessageType.HostList:
                        var list = JsonSerializer.Deserialize<HostListMessage>(payload);
                        HostListReceived?.Invoke(this, list.Hosts);
                        break;

                    case MessageType.HostOnline:
                        var online = JsonSerializer.Deserialize<HostOnlineMessage>(payload);
                        HostCameOnline?.Invoke(this, online);
                        break;

                    case MessageType.HostOffline:
                        var offline = JsonSerializer.Deserialize<HostOfflineMessage>(payload);
                        HostWentOffline?.Invoke(this, offline);
                        break;

                    case MessageType.SessionStarted:
                        var session = JsonSerializer.Deserialize<SessionStartedMessage>(payload);
                        SessionStarted?.Invoke(this, session);
                        break;

                    case MessageType.FrameData:
                        FrameReceived?.Invoke(this, payload);
                        break;

                    case MessageType.Disconnect:
                        Disconnected?.Invoke(this, "Relay closed the connection.");
                        return;

                    case MessageType.ClipboardSync:
                        var clipText = System.Text.Encoding.UTF8.GetString(payload);
                        ClipboardReceived?.Invoke(this, clipText);
                        break;

                    case MessageType.MonitorList:
                        var monitors = JsonSerializer.Deserialize<MonitorListMessage>(payload);
                        MonitorListReceived?.Invoke(this, monitors.Monitors);
                        break;

                    case MessageType.ChatMessage:
                        var chatMsg = JsonSerializer.Deserialize<ChatMessageData>(payload);
                        ChatReceived?.Invoke(this, chatMsg);
                        break;

                    case MessageType.Heartbeat:
                        break;
                }
            }
        }
        catch (Exception ex)
        {
            Disconnected?.Invoke(this, ex.Message);
        }
    }

    // ---- Input forwarding (sent to relay, which forwards to host) ----

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

    // ---- Clipboard sync ----

    public async Task SendClipboardAsync(string text)
    {
        if (_stream is null) return;
        var bytes = System.Text.Encoding.UTF8.GetBytes(text);
        await MessageFraming.WriteFrameAsync(_stream, MessageType.ClipboardSync, bytes);
    }

    // ---- Chat ----

    public Task SendChatAsync(string senderName, string text) =>
        SendAsync(MessageType.ChatMessage, new ChatMessageData(senderName, text, DateTime.UtcNow));

    // ---- Monitor selection ----

    public Task SelectMonitorAsync(int monitorIndex) =>
        SendAsync(MessageType.MonitorSelect, new MonitorSelectMessage(monitorIndex));

    // ---- File transfer ----

    public Stream? GetStream() => _stream;

    public async ValueTask DisposeAsync()
    {
        _cts?.Cancel();
        if (_stream is not null)
        {
            try { await MessageFraming.WriteFrameAsync(_stream, MessageType.Disconnect, Array.Empty<byte>()); }
            catch { /* best effort */ }
        }
        _stream?.Dispose();
        if (_ws is not null)
        {
            try { _ws.Dispose(); }
            catch { /* best effort */ }
        }
    }
}
