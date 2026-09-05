using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text.Json;
using RemoteAdminTool.Shared;

namespace RemoteAdminTool.RelayServer;

/// <summary>
/// Central hub that tracks all connected host agents and authenticated admin connections.
/// When an admin requests a session with a host, the hub pairs them and hands off
/// to a SessionRelay for bidirectional frame/input forwarding.
/// </summary>
public sealed class ConnectionHub
{
    private readonly string _adminAuthToken;
    private readonly string _hostAuthToken;

    // All connected hosts, keyed by MachineId
    private readonly ConcurrentDictionary<string, ConnectedHost> _hosts = new();

    // The currently connected admin (single admin model for now)
    private ConnectedAdmin? _admin;
    private readonly object _adminLock = new();

    // Active session (admin ↔ host relay), if any
    private SessionRelay? _activeSession;

    public ConnectionHub(string adminAuthToken, string hostAuthToken)
    {
        _adminAuthToken = adminAuthToken;
        _hostAuthToken = hostAuthToken;
    }

    /// <summary>
    /// Handle a new WebSocket connection. First message determines if this is a Host or Admin.
    /// </summary>
    public async Task HandleConnectionAsync(WebSocket ws, CancellationToken ct)
    {
        using var stream = new WebSocketStreamAdapter(ws);

        // Read the first message to determine connection type
        var (type, payload) = await MessageFraming.ReadFrameAsync(stream, ct);

        switch (type)
        {
            case MessageType.HostRegister:
                await HandleHostConnectionAsync(ws, stream, payload, ct);
                break;

            case MessageType.AdminAuth:
                await HandleAdminConnectionAsync(ws, stream, payload, ct);
                break;

            default:
                // Unknown first message — close
                await ws.CloseAsync(WebSocketCloseStatus.InvalidPayloadData,
                    "Expected HostRegister or AdminAuth as first message", ct);
                break;
        }
    }

    private async Task HandleHostConnectionAsync(WebSocket ws, WebSocketStreamAdapter stream,
        byte[] payload, CancellationToken ct)
    {
        var register = JsonSerializer.Deserialize<HostRegisterMessage>(payload);

        // Validate host auth token
        if (register.AuthToken != _hostAuthToken)
        {
            var reject = new HostRegisterAckMessage(false, "Invalid host auth token");
            await MessageFraming.WriteFrameAsync(stream, MessageType.HostRegisterAck,
                JsonSerializer.SerializeToUtf8Bytes(reject), ct);
            await ws.CloseAsync(WebSocketCloseStatus.PolicyViolation, "Auth failed", ct);
            return;
        }

        var host = new ConnectedHost
        {
            MachineId = register.MachineId,
            MachineName = register.MachineName,
            OsVersion = register.OsVersion,
            WebSocket = ws,
            Stream = stream,
            ConnectedAt = DateTime.UtcNow
        };

        _hosts[register.MachineId] = host;
        Console.WriteLine($"[Hub] Host registered: {register.MachineName} ({register.MachineId})");

        // Send ack
        var ack = new HostRegisterAckMessage(true, null);
        await MessageFraming.WriteFrameAsync(stream, MessageType.HostRegisterAck,
            JsonSerializer.SerializeToUtf8Bytes(ack), ct);

        // Notify admin if connected
        await NotifyAdminHostOnlineAsync(host, ct);

        // Keep the host connection alive, waiting for session requests from the hub
        try
        {
            await HostIdleLoopAsync(host, ct);
        }
        finally
        {
            _hosts.TryRemove(register.MachineId, out _);
            Console.WriteLine($"[Hub] Host disconnected: {register.MachineName} ({register.MachineId})");
            await NotifyAdminHostOfflineAsync(host, ct);
        }
    }

    /// <summary>
    /// Host sits here after registering, waiting for commands from the relay (e.g., start session).
    /// Also handles heartbeats to detect stale connections.
    /// </summary>
    private async Task HostIdleLoopAsync(ConnectedHost host, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && host.WebSocket.State == WebSocketState.Open)
        {
            var (type, payload) = await MessageFraming.ReadFrameAsync(host.Stream, ct);

            switch (type)
            {
                case MessageType.Heartbeat:
                    host.LastHeartbeat = DateTime.UtcNow;
                    break;

                case MessageType.Disconnect:
                    return;

                default:
                    // If there's an active session involving this host, the SessionRelay handles
                    // messages. Any stray messages here during idle are ignored.
                    break;
            }
        }
    }

    private async Task HandleAdminConnectionAsync(WebSocket ws, WebSocketStreamAdapter stream,
        byte[] payload, CancellationToken ct)
    {
        var auth = JsonSerializer.Deserialize<AdminAuthMessage>(payload);

        if (auth.AuthToken != _adminAuthToken)
        {
            var reject = new AdminAuthAckMessage(false, "Invalid admin auth token");
            await MessageFraming.WriteFrameAsync(stream, MessageType.AdminAuthAck,
                JsonSerializer.SerializeToUtf8Bytes(reject), ct);
            await ws.CloseAsync(WebSocketCloseStatus.PolicyViolation, "Auth failed", ct);
            return;
        }

        var admin = new ConnectedAdmin
        {
            WebSocket = ws,
            Stream = stream
        };

        lock (_adminLock)
        {
            _admin = admin;
        }

        Console.WriteLine("[Hub] Admin connected and authenticated");

        // Send auth ack
        var ack = new AdminAuthAckMessage(true, null);
        await MessageFraming.WriteFrameAsync(stream, MessageType.AdminAuthAck,
            JsonSerializer.SerializeToUtf8Bytes(ack), ct);

        // Send current host list
        await SendHostListAsync(stream, ct);

        // Admin command loop
        try
        {
            await AdminCommandLoopAsync(admin, ct);
        }
        finally
        {
            lock (_adminLock)
            {
                if (_admin == admin) _admin = null;
            }

            // End any active session
            if (_activeSession is not null)
            {
                _activeSession.Stop();
                _activeSession = null;
            }

            Console.WriteLine("[Hub] Admin disconnected");
        }
    }

    private async Task AdminCommandLoopAsync(ConnectedAdmin admin, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && admin.WebSocket.State == WebSocketState.Open)
        {
            var (type, payload) = await MessageFraming.ReadFrameAsync(admin.Stream, ct);

            switch (type)
            {
                case MessageType.RequestHostList:
                    await SendHostListAsync(admin.Stream, ct);
                    break;

                case MessageType.ConnectToHost:
                    var connect = JsonSerializer.Deserialize<ConnectToHostMessage>(payload);
                    await StartSessionAsync(admin, connect.MachineId, ct);
                    break;

                case MessageType.DisconnectFromHost:
                    if (_activeSession is not null)
                    {
                        _activeSession.Stop();
                        _activeSession = null;
                        Console.WriteLine("[Hub] Admin ended session");
                    }
                    break;

                case MessageType.Heartbeat:
                    break;

                case MessageType.Disconnect:
                    return;

                default:
                    // During an active session, input messages (MouseMove, KeyEvent, etc.)
                    // from admin should be relayed to host. Forward them.
                    if (_activeSession is not null)
                    {
                        await _activeSession.ForwardToHostAsync(type, payload, ct);
                    }
                    break;
            }
        }
    }

    private async Task StartSessionAsync(ConnectedAdmin admin, string machineId, CancellationToken ct)
    {
        if (!_hosts.TryGetValue(machineId, out var host))
        {
            // Host not found — send a session-started with empty data to indicate failure
            Console.WriteLine($"[Hub] Session request for unknown host: {machineId}");
            return;
        }

        // End any existing session
        if (_activeSession is not null)
        {
            _activeSession.Stop();
            _activeSession = null;
        }

        Console.WriteLine($"[Hub] Starting session: Admin ↔ {host.MachineName} ({machineId})");

        // Tell the host a session is starting (it should begin sending frames)
        var hostNotify = new SessionStartedMessage(machineId, host.MachineName, 0, 0);
        await MessageFraming.WriteFrameAsync(host.Stream, MessageType.SessionStarted,
            JsonSerializer.SerializeToUtf8Bytes(hostNotify), ct);

        // Create the bidirectional relay
        _activeSession = new SessionRelay(admin, host);
        _ = _activeSession.RunHostToAdminRelayAsync(ct);

        // Tell the admin session is started
        // (screen dimensions will come with the first FrameData or can be sent separately)
        var adminNotify = new SessionStartedMessage(machineId, host.MachineName, 0, 0);
        await MessageFraming.WriteFrameAsync(admin.Stream, MessageType.SessionStarted,
            JsonSerializer.SerializeToUtf8Bytes(adminNotify), ct);
    }

    private async Task SendHostListAsync(Stream stream, CancellationToken ct)
    {
        var hostInfos = _hosts.Values.Select(h => new HostInfoMessage(
            h.MachineId,
            h.MachineName,
            h.OsVersion,
            h.WebSocket.State == WebSocketState.Open,
            h.LastHeartbeat
        )).ToArray();

        var list = new HostListMessage(hostInfos);
        await MessageFraming.WriteFrameAsync(stream, MessageType.HostList,
            JsonSerializer.SerializeToUtf8Bytes(list), ct);
    }

    private async Task NotifyAdminHostOnlineAsync(ConnectedHost host, CancellationToken ct)
    {
        ConnectedAdmin? admin;
        lock (_adminLock) { admin = _admin; }

        if (admin?.WebSocket.State == WebSocketState.Open)
        {
            try
            {
                var msg = new HostOnlineMessage(host.MachineId, host.MachineName);
                await MessageFraming.WriteFrameAsync(admin.Stream, MessageType.HostOnline,
                    JsonSerializer.SerializeToUtf8Bytes(msg), ct);
            }
            catch { /* admin may have disconnected */ }
        }
    }

    private async Task NotifyAdminHostOfflineAsync(ConnectedHost host, CancellationToken ct)
    {
        ConnectedAdmin? admin;
        lock (_adminLock) { admin = _admin; }

        if (admin?.WebSocket.State == WebSocketState.Open)
        {
            try
            {
                var msg = new HostOfflineMessage(host.MachineId, host.MachineName);
                await MessageFraming.WriteFrameAsync(admin.Stream, MessageType.HostOffline,
                    JsonSerializer.SerializeToUtf8Bytes(msg), ct);
            }
            catch { /* admin may have disconnected */ }
        }
    }
}

public sealed class ConnectedHost
{
    public required string MachineId { get; init; }
    public required string MachineName { get; init; }
    public required string OsVersion { get; init; }
    public required WebSocket WebSocket { get; init; }
    public required WebSocketStreamAdapter Stream { get; init; }
    public DateTime ConnectedAt { get; init; }
    public DateTime LastHeartbeat { get; set; } = DateTime.UtcNow;
}

public sealed class ConnectedAdmin
{
    public required WebSocket WebSocket { get; init; }
    public required WebSocketStreamAdapter Stream { get; init; }
}
