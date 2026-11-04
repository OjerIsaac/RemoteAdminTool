using System.IO;
using System.Net.WebSockets;
using System.Text.Json;
using RemoteAdminTool.Shared;

namespace RemoteAdminTool.Viewer;

/// <summary>
/// Admin viewer client that connects to the relay server, authenticates as admin or super admin,
/// receives the list of online hosts, and can start sessions with any of them.
/// 
/// Super admin (empty LicenseKey + matching super admin token): sees all tenants and all hosts.
/// Sub-admin (LicenseKey set): sees only hosts in their tenant.
/// </summary>
public sealed class ViewerClient : IAsyncDisposable
{
    private ClientWebSocket? _ws;
    private WebSocketStreamAdapter? _stream;
    private CancellationTokenSource? _cts;

    public bool IsSuperAdmin { get; private set; }

    public event EventHandler<byte[]>? FrameReceived;
    public event EventHandler<HostInfoMessage[]>? HostListReceived;
    public event EventHandler<HostOnlineMessage>? HostCameOnline;
    public event EventHandler<HostOfflineMessage>? HostWentOffline;
    public event EventHandler<SessionStartedMessage>? SessionStarted;
    public event EventHandler<MonitorInfoData[]>? MonitorListReceived;
    public event EventHandler<string>? Disconnected;
    public event EventHandler<TenantInfoMessage[]>? TenantListReceived;
    public event EventHandler<CreateTenantAckMessage>? TenantCreated;

    // ---- Remote Console + Monitor events ----
    public event EventHandler<ShellOutputMessage>? ShellOutputReceived;
    public event EventHandler? ShellStarted;
    public event EventHandler? ShellEnded;
    public event EventHandler<SystemStatsMessage>? SystemStatsReceived;
    public event EventHandler<SystemInventoryMessage>? SystemInventoryReceived;

    // ---- New feature events ----
    public event EventHandler<DeviceInfoMessage>? DeviceInfoReceived;
    public event EventHandler<DirectoryListResponseMessage>? DirectoryListReceived;
    public event EventHandler<FileTransferRequestMessage>? FileTransferRequestReceived;

    /// <summary>
    /// Connect to the relay server and authenticate.
    /// - Sub-admins: pass their LicenseKey and AdminToken.
    /// - Super admin: pass empty/null LicenseKey and the super admin token.
    /// Returns (Accepted, RejectReason, IsSuperAdmin).
    /// </summary>
    public async Task<(bool Accepted, string? RejectReason, bool IsSuperAdmin)> ConnectAsync(
        string relayUrl, string adminToken, string? licenseKey = null, CancellationToken ct = default)
    {
        _ws = new ClientWebSocket();
        await _ws.ConnectAsync(new Uri(relayUrl), ct);
        _stream = new WebSocketStreamAdapter(_ws);

        var isSuperAdmin = string.IsNullOrEmpty(licenseKey);
        var auth = new AdminAuthMessage(adminToken, licenseKey, isSuperAdmin);
        await MessageFraming.WriteFrameAsync(_stream, MessageType.AdminAuth,
            JsonSerializer.SerializeToUtf8Bytes(auth), ct);

        var (type, payload) = await MessageFraming.ReadFrameAsync(_stream, ct);
        if (type != MessageType.AdminAuthAck)
            throw new InvalidDataException("Expected AdminAuthAck from relay.");

        var ack = JsonSerializer.Deserialize<AdminAuthAckMessage>(payload);
        if (ack.Accepted)
        {
            IsSuperAdmin = ack.IsSuperAdmin;
            _cts = new CancellationTokenSource();
            _ = ReceiveLoopAsync(_cts.Token);
        }

        return (ack.Accepted, ack.RejectReason, ack.IsSuperAdmin);
    }

    /// <summary>Request the relay to start a session with a specific host.</summary>
    public async Task ConnectToHostAsync(string machineId)
    {
        if (_stream is null) return;
        var msg = new ConnectToHostMessage(machineId);
        await MessageFraming.WriteFrameAsync(_stream, MessageType.ConnectToHost,
            JsonSerializer.SerializeToUtf8Bytes(msg));
    }

    /// <summary>Tell the relay to end the current session.</summary>
    public async Task DisconnectFromHostAsync(string machineId)
    {
        if (_stream is null) return;
        var msg = new DisconnectFromHostMessage(machineId);
        await MessageFraming.WriteFrameAsync(_stream, MessageType.DisconnectFromHost,
            JsonSerializer.SerializeToUtf8Bytes(msg));
    }

    /// <summary>Request an updated host list from the relay.</summary>
    public async Task RequestHostListAsync()
    {
        if (_stream is null) return;
        await MessageFraming.WriteFrameAsync(_stream, MessageType.RequestHostList,
            Array.Empty<byte>());
    }

    // ---- Super admin commands ----

    /// <summary>Request the list of all tenants. Only works when IsSuperAdmin = true.</summary>
    public async Task RequestTenantListAsync()
    {
        if (_stream is null || !IsSuperAdmin) return;
        await MessageFraming.WriteFrameAsync(_stream, MessageType.TenantList,
            Array.Empty<byte>());
    }

    /// <summary>Create a new sub-admin tenant. Only works when IsSuperAdmin = true.</summary>
    public async Task CreateTenantAsync(string displayName, string adminEmail)
    {
        if (_stream is null || !IsSuperAdmin) return;
        var msg = new CreateTenantMessage(displayName, adminEmail);
        await MessageFraming.WriteFrameAsync(_stream, MessageType.CreateTenant,
            JsonSerializer.SerializeToUtf8Bytes(msg));
    }

    /// <summary>Suspend a tenant (their hosts will be rejected). Super admin only.</summary>
    public async Task SuspendTenantAsync(string tenantId)
    {
        if (_stream is null || !IsSuperAdmin) return;
        await MessageFraming.WriteFrameAsync(_stream, MessageType.SuspendTenant,
            JsonSerializer.SerializeToUtf8Bytes(new SuspendTenantMessage(tenantId)));
    }

    /// <summary>Re-activate a suspended tenant. Super admin only.</summary>
    public async Task ResumeTenantAsync(string tenantId)
    {
        if (_stream is null || !IsSuperAdmin) return;
        await MessageFraming.WriteFrameAsync(_stream, MessageType.ResumeTenant,
            JsonSerializer.SerializeToUtf8Bytes(new ResumeTenantMessage(tenantId)));
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

                    case MessageType.MonitorList:
                        var monitors = JsonSerializer.Deserialize<MonitorListMessage>(payload);
                        MonitorListReceived?.Invoke(this, monitors.Monitors);
                        break;

                    case MessageType.Heartbeat:
                        break;

                    // ---- Super admin tenant messages ----
                    case MessageType.TenantList:
                        var tenantList = JsonSerializer.Deserialize<TenantListMessage>(payload);
                        TenantListReceived?.Invoke(this, tenantList.Tenants);
                        break;

                    case MessageType.CreateTenantAck:
                        var tenantAck = JsonSerializer.Deserialize<CreateTenantAckMessage>(payload);
                        TenantCreated?.Invoke(this, tenantAck);
                        break;

                    // ---- Remote Console ----
                    case MessageType.ShellStarted:
                        ShellStarted?.Invoke(this, EventArgs.Empty);
                        break;

                    case MessageType.ShellEnded:
                        ShellEnded?.Invoke(this, EventArgs.Empty);
                        break;

                    case MessageType.ShellOutput:
                        var shellOut = JsonSerializer.Deserialize<ShellOutputMessage>(payload);
                        ShellOutputReceived?.Invoke(this, shellOut);
                        break;

                    // ---- System Monitor ----
                    case MessageType.SystemStats:
                        var stats = JsonSerializer.Deserialize<SystemStatsMessage>(payload);
                        SystemStatsReceived?.Invoke(this, stats);
                        break;

                    case MessageType.SystemInventory:
                        var inventory = JsonSerializer.Deserialize<SystemInventoryMessage>(payload);
                        SystemInventoryReceived?.Invoke(this, inventory);
                        break;

                    // ---- Device Info ----
                    case MessageType.DeviceInfo:
                        var deviceInfo = JsonSerializer.Deserialize<DeviceInfoMessage>(payload);
                        DeviceInfoReceived?.Invoke(this, deviceInfo);
                        break;

                    // ---- File Manager ----
                    case MessageType.DirectoryListResponse:
                        var dirResp = JsonSerializer.Deserialize<DirectoryListResponseMessage>(payload);
                        DirectoryListReceived?.Invoke(this, dirResp);
                        break;

                    case MessageType.FileTransferRequest:
                        var ftReq = JsonSerializer.Deserialize<FileTransferRequestMessage>(payload);
                        FileTransferRequestReceived?.Invoke(this, ftReq);
                        break;
                }
            }
        }
        catch (Exception ex)
        {
            Disconnected?.Invoke(this, ex.Message);
        }
    }

    // ---- Input forwarding ----

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

    // ---- Monitor selection ----

    public Task SelectMonitorAsync(int monitorIndex) =>
        SendAsync(MessageType.MonitorSelect, new MonitorSelectMessage(monitorIndex));

    // ---- Remote Console ----

    /// <summary>Send a line of text to the remote shell stdin.</summary>
    public Task SendShellInputAsync(string input) =>
        SendAsync(MessageType.ShellInput, new ShellInputMessage(input));

    /// <summary>Request the Host to relaunch with elevated Administrator rights.</summary>
    public Task ElevateHostAsync() =>
        SendShellInputAsync("##ELEVATE_HOST");

    // ---- System Monitor ----

    /// <summary>Ask the Host to start or stop streaming system stats.</summary>
    public Task SendStatsRequestAsync(bool enable) =>
        SendAsync(MessageType.StatsRequest, new StatsRequestMessage(enable));

    /// <summary>Request the Host to terminate a process by PID.</summary>
    public Task SendKillProcessAsync(int pid) =>
        SendAsync(MessageType.KillProcess, new KillProcessMessage(pid));

    /// <summary>Request fresh DeviceInfo and SystemInventory snapshots from the host.</summary>
    public Task RequestDeviceInfoAsync() =>
        SendAsync(MessageType.DeviceInfoRequest, Array.Empty<byte>());

    // ---- Silent mode ----

    /// <summary>Ask the host to enable/disable silent mode (no consent dialog).</summary>
    public Task SetSilentModeAsync(bool silent) =>
        SendAsync(MessageType.SetSilentMode, new SetSilentModeMessage(silent));

    // ---- File Manager ----

    /// <summary>Request a directory listing from the host.</summary>
    public Task RequestDirectoryAsync(string path) =>
        SendAsync(MessageType.DirectoryList, new DirectoryListMessage(path));

    /// <summary>Delete a file or folder on the host.</summary>
    public Task DeleteRemotePathAsync(string path) =>
        SendAsync(MessageType.FileDelete, new FileDeleteMessage(path));

    /// <summary>Send a local file to the host at the given destination path.</summary>
    public async Task SendFileToHostAsync(string localPath, string remotePath, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        if (_stream is null) return;
        var svc = new FileTransferService();
        await svc.SendFileAsync(_stream, localPath, remotePath, progress, ct);
    }

    /// <summary>Request the host to send a file to us (download from host).</summary>
    public async Task<string> DownloadFileFromHostAsync(string remotePath, string localSaveDir, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        if (_stream is null) throw new InvalidOperationException("Not connected.");
        // Send a transfer request - host will respond with FileTransferData chunks
        var req = new FileTransferRequestMessage(
            Guid.NewGuid().ToString("N")[..8], Path.GetFileName(remotePath), 0, remotePath);
        await MessageFraming.WriteFrameAsync(_stream, MessageType.FileTransferRequest,
            JsonSerializer.SerializeToUtf8Bytes(req));
        // Receive
        var svc = new FileTransferService();
        return await svc.ReceiveFileAsync(_stream, req, localSaveDir, progress, ct);
    }

    // ---- File transfer (legacy) ----

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
