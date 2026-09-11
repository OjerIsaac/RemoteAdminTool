using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RemoteAdminTool.Shared;

namespace RemoteAdminTool.RelayServer;

/// <summary>
/// Multi-tenant central hub.
/// 
/// Tenant isolation:
///   - Each ConnectedHost is stored under its tenant's LicenseKey.
///   - Sub-admins only see hosts in their own tenant.
///   - The super admin (matching SuperAdminToken) sees ALL tenants and ALL hosts.
///   - Super admin can create/suspend/resume tenants and get the full tenant list.
/// 
/// Sessions are per-tenant — an admin can only start a session with a host in their tenant.
/// </summary>
public sealed class ConnectionHub
{
    private readonly string _superAdminToken;
    private readonly TenantStore _tenantStore;
    private readonly IDbContextFactory<RelayDbContext> _dbFactory;

    // tenant-scoped host registry: tenantId -> (machineId -> ConnectedHost)
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, ConnectedHost>> _tenantHosts = new();

    // tenant-scoped admin registry: tenantId -> ConnectedAdmin
    // super admin is stored under the special key "*"
    private const string SuperAdminKey = "*";
    private readonly ConcurrentDictionary<string, ConnectedAdmin> _admins = new();

    // active sessions per tenant: tenantId -> SessionRelay
    private readonly ConcurrentDictionary<string, SessionRelay> _activeSessions = new();

    private readonly object _notifyLock = new();

    public ConnectionHub(string superAdminToken, TenantStore tenantStore,
        IDbContextFactory<RelayDbContext> dbFactory)
    {
        _superAdminToken = superAdminToken;
        _tenantStore = tenantStore;
        _dbFactory = dbFactory;
    }

    /// <summary>
    /// Entry point for every new WebSocket connection. Reads the first frame to determine role.
    /// </summary>
    public async Task HandleConnectionAsync(WebSocket ws, CancellationToken ct)
    {
        using var stream = new WebSocketStreamAdapter(ws);

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
                await ws.CloseAsync(WebSocketCloseStatus.InvalidPayloadData,
                    "Expected HostRegister or AdminAuth as first message", ct);
                break;
        }
    }

    // -------------------------------------------------------------------------
    // HOST SIDE
    // -------------------------------------------------------------------------

    private async Task HandleHostConnectionAsync(WebSocket ws, WebSocketStreamAdapter stream,
        byte[] payload, CancellationToken ct)
    {
        var register = JsonSerializer.Deserialize<HostRegisterMessage>(payload);

        // 1. Validate license key -> tenant
        var tenant = await _tenantStore.GetByLicenseKeyAsync(register.LicenseKey);
        if (tenant is null || !tenant.IsActive)
        {
            var reject = new HostRegisterAckMessage(false,
                tenant is null ? "Unknown license key" : "Tenant is suspended");
            await MessageFraming.WriteFrameAsync(stream, MessageType.HostRegisterAck,
                JsonSerializer.SerializeToUtf8Bytes(reject), ct);
            await ws.CloseAsync(WebSocketCloseStatus.PolicyViolation, "Auth failed", ct);
            return;
        }

        // 2. Validate host auth token
        if (register.AuthToken != GetHostTokenForTenant(tenant))
        {
            var reject = new HostRegisterAckMessage(false, "Invalid host auth token");
            await MessageFraming.WriteFrameAsync(stream, MessageType.HostRegisterAck,
                JsonSerializer.SerializeToUtf8Bytes(reject), ct);
            await ws.CloseAsync(WebSocketCloseStatus.PolicyViolation, "Auth failed", ct);
            return;
        }

        // 3. Seat limit + MachineId tracking
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var knownHost = await db.RegisteredHosts
            .FirstOrDefaultAsync(h => h.TenantId == tenant.TenantId
                                   && h.MachineId == register.MachineId, ct);

        if (knownHost is null)
        {
            // New machine — check seat limit before allowing it
            if (tenant.MaxHosts > 0)
            {
                var currentCount = await db.RegisteredHosts
                    .CountAsync(h => h.TenantId == tenant.TenantId && h.IsAllowed, ct);

                if (currentCount >= tenant.MaxHosts)
                {
                    var reject = new HostRegisterAckMessage(false,
                        $"Seat limit reached ({tenant.MaxHosts} hosts). Contact your admin.");
                    await MessageFraming.WriteFrameAsync(stream, MessageType.HostRegisterAck,
                        JsonSerializer.SerializeToUtf8Bytes(reject), ct);
                    await ws.CloseAsync(WebSocketCloseStatus.PolicyViolation, "Seat limit reached", ct);
                    return;
                }
            }

            // Register new machine in DB
            db.RegisteredHosts.Add(new RegisteredHost
            {
                TenantId   = tenant.TenantId,
                MachineId  = register.MachineId,
                MachineName = register.MachineName,
                FirstSeenAt = DateTime.UtcNow,
                LastSeenAt  = DateTime.UtcNow,
                IsAllowed   = true
            });
            await db.SaveChangesAsync(ct);
            Console.WriteLine($"[Hub] New machine registered: {register.MachineName} ({register.MachineId}) | Tenant: {tenant.DisplayName}");
        }
        else
        {
            // Known machine — check if it's been banned
            if (!knownHost.IsAllowed)
            {
                var reject = new HostRegisterAckMessage(false, "This machine has been blocked by the admin.");
                await MessageFraming.WriteFrameAsync(stream, MessageType.HostRegisterAck,
                    JsonSerializer.SerializeToUtf8Bytes(reject), ct);
                await ws.CloseAsync(WebSocketCloseStatus.PolicyViolation, "Machine blocked", ct);
                return;
            }

            // Update last seen
            knownHost.LastSeenAt  = DateTime.UtcNow;
            knownHost.MachineName = register.MachineName; // name may change if user renamed PC
            await db.SaveChangesAsync(ct);
        }

        var host = new ConnectedHost
        {
            MachineId = register.MachineId,
            MachineName = register.MachineName,
            OsVersion = register.OsVersion,
            TenantId = tenant.TenantId,
            WebSocket = ws,
            Stream = stream,
            ConnectedAt = DateTime.UtcNow
        };

        var tenantHosts = _tenantHosts.GetOrAdd(tenant.TenantId, _ => new ConcurrentDictionary<string, ConnectedHost>());
        tenantHosts[register.MachineId] = host;

        Console.WriteLine($"[Hub] Host connected: {register.MachineName} ({register.MachineId}) | Tenant: {tenant.DisplayName}");

        // Ack
        var ack = new HostRegisterAckMessage(true, null);
        await MessageFraming.WriteFrameAsync(stream, MessageType.HostRegisterAck,
            JsonSerializer.SerializeToUtf8Bytes(ack), ct);

        await NotifyAdminHostOnlineAsync(host, tenant, ct);

        try
        {
            await HostIdleLoopAsync(host, ct);
        }
        finally
        {
            tenantHosts.TryRemove(register.MachineId, out _);
            Console.WriteLine($"[Hub] Host disconnected: {register.MachineName} ({register.MachineId})");
            await NotifyAdminHostOfflineAsync(host, tenant, ct);
        }
    }

    /// <summary>
    /// Host auth token: for now all tenants share the global host token from config.
    /// Future: each tenant could have its own host token stored in the Tenant record.
    /// </summary>
    private string GetHostTokenForTenant(Tenant tenant)
    {
        // Read from environment variable / config — the same global host token for now.
        return Environment.GetEnvironmentVariable("Auth__HostToken")
            ?? throw new InvalidOperationException("Auth__HostToken not configured");
    }

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
            }
        }
    }

    // -------------------------------------------------------------------------
    // ADMIN SIDE
    // -------------------------------------------------------------------------

    private async Task HandleAdminConnectionAsync(WebSocket ws, WebSocketStreamAdapter stream,
        byte[] payload, CancellationToken ct)
    {
        var auth = JsonSerializer.Deserialize<AdminAuthMessage>(payload);

        // Determine if this is the super admin or a sub-admin
        bool isSuperAdmin = auth.AuthToken == _superAdminToken;
        Tenant? tenant = null;

        if (!isSuperAdmin)
        {
            tenant = await _tenantStore.GetByAdminTokenAsync(auth.AuthToken);
            if (tenant is null || !tenant.IsActive)
            {
                var reject = new AdminAuthAckMessage(false,
                    tenant is null ? "Invalid admin token" : "Tenant is suspended", false);
                await MessageFraming.WriteFrameAsync(stream, MessageType.AdminAuthAck,
                    JsonSerializer.SerializeToUtf8Bytes(reject), ct);
                await ws.CloseAsync(WebSocketCloseStatus.PolicyViolation, "Auth failed", ct);
                return;
            }
        }

        var tenantKey = isSuperAdmin ? SuperAdminKey : tenant!.TenantId;

        var admin = new ConnectedAdmin
        {
            WebSocket = ws,
            Stream = stream,
            TenantId = tenantKey,
            IsSuperAdmin = isSuperAdmin
        };

        _admins[tenantKey] = admin;

        Console.WriteLine($"[Hub] Admin connected: {(isSuperAdmin ? "SUPER ADMIN" : tenant!.DisplayName)}");

        // Ack
        var ack = new AdminAuthAckMessage(true, null, isSuperAdmin);
        await MessageFraming.WriteFrameAsync(stream, MessageType.AdminAuthAck,
            JsonSerializer.SerializeToUtf8Bytes(ack), ct);

        // Send initial host list (scoped)
        await SendHostListAsync(admin, ct);

        try
        {
            await AdminCommandLoopAsync(admin, ct);
        }
        finally
        {
            _admins.TryRemove(tenantKey, out _);

            if (_activeSessions.TryRemove(tenantKey, out var session))
                session.Stop();

            Console.WriteLine($"[Hub] Admin disconnected: {(isSuperAdmin ? "SUPER ADMIN" : tenant?.DisplayName)}");
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
                    await SendHostListAsync(admin, ct);
                    break;

                case MessageType.ConnectToHost:
                    var connect = JsonSerializer.Deserialize<ConnectToHostMessage>(payload);
                    await StartSessionAsync(admin, connect.MachineId, ct);
                    break;

                case MessageType.DisconnectFromHost:
                    if (_activeSessions.TryRemove(admin.TenantId, out var session))
                    {
                        session.Stop();
                        Console.WriteLine("[Hub] Admin ended session");
                    }
                    break;

                case MessageType.Heartbeat:
                    break;

                case MessageType.Disconnect:
                    return;

                // ---- Super admin commands ----
                case MessageType.TenantList when admin.IsSuperAdmin:
                    await SendTenantListAsync(admin, ct);
                    break;

                case MessageType.CreateTenant when admin.IsSuperAdmin:
                    var create = JsonSerializer.Deserialize<CreateTenantMessage>(payload);
                    await HandleCreateTenantAsync(admin, create, ct);
                    break;

                case MessageType.SuspendTenant when admin.IsSuperAdmin:
                    var suspend = JsonSerializer.Deserialize<SuspendTenantMessage>(payload);
                    await _tenantStore.SetActiveAsync(suspend.TenantId, false);
                    Console.WriteLine($"[Hub] Tenant suspended: {suspend.TenantId}");
                    break;

                case MessageType.ResumeTenant when admin.IsSuperAdmin:
                    var resume = JsonSerializer.Deserialize<ResumeTenantMessage>(payload);
                    await _tenantStore.SetActiveAsync(resume.TenantId, true);
                    Console.WriteLine($"[Hub] Tenant resumed: {resume.TenantId}");
                    break;

                default:
                    // Forward input events to host during active session
                    if (_activeSessions.TryGetValue(admin.TenantId, out var activeSession))
                        await activeSession.ForwardToHostAsync(type, payload, ct);
                    break;
            }
        }
    }

    // -------------------------------------------------------------------------
    // SESSIONS
    // -------------------------------------------------------------------------

    private async Task StartSessionAsync(ConnectedAdmin admin, string machineId, CancellationToken ct)
    {
        // Determine which tenant's host pool to look in
        ConcurrentDictionary<string, ConnectedHost>? hosts = null;

        if (admin.IsSuperAdmin)
        {
            // Super admin can connect to any host — search all tenants
            foreach (var (_, pool) in _tenantHosts)
            {
                if (pool.ContainsKey(machineId)) { hosts = pool; break; }
            }
        }
        else
        {
            _tenantHosts.TryGetValue(admin.TenantId, out hosts);
        }

        if (hosts is null || !hosts.TryGetValue(machineId, out var host))
        {
            Console.WriteLine($"[Hub] Session request for unknown host: {machineId}");
            return;
        }

        if (_activeSessions.TryRemove(admin.TenantId, out var old))
            old.Stop();

        Console.WriteLine($"[Hub] Starting session: Admin ({admin.TenantId}) <-> {host.MachineName}");

        var hostNotify = new SessionStartedMessage(machineId, host.MachineName, 0, 0);
        await MessageFraming.WriteFrameAsync(host.Stream, MessageType.SessionStarted,
            JsonSerializer.SerializeToUtf8Bytes(hostNotify), ct);

        var relay = new SessionRelay(admin, host);
        _activeSessions[admin.TenantId] = relay;
        _ = relay.RunHostToAdminRelayAsync(ct);

        var adminNotify = new SessionStartedMessage(machineId, host.MachineName, 0, 0);
        await MessageFraming.WriteFrameAsync(admin.Stream, MessageType.SessionStarted,
            JsonSerializer.SerializeToUtf8Bytes(adminNotify), ct);
    }

    // -------------------------------------------------------------------------
    // HOST LIST
    // -------------------------------------------------------------------------

    private async Task SendHostListAsync(ConnectedAdmin admin, CancellationToken ct)
    {
        HostInfoMessage[] hosts;

        if (admin.IsSuperAdmin)
        {
            hosts = _tenantHosts
                .SelectMany(kv => kv.Value.Values.Select(h => ToHostInfo(h)))
                .ToArray();
        }
        else
        {
            hosts = _tenantHosts.TryGetValue(admin.TenantId, out var pool)
                ? pool.Values.Select(h => ToHostInfo(h)).ToArray()
                : Array.Empty<HostInfoMessage>();
        }

        var list = new HostListMessage(hosts);
        await MessageFraming.WriteFrameAsync(admin.Stream, MessageType.HostList,
            JsonSerializer.SerializeToUtf8Bytes(list), ct);
    }

    private static HostInfoMessage ToHostInfo(ConnectedHost h) => new(
        h.MachineId, h.MachineName, h.OsVersion,
        h.WebSocket.State == WebSocketState.Open,
        h.LastHeartbeat, h.TenantId);

    // -------------------------------------------------------------------------
    // TENANT LIST (super admin only)
    // -------------------------------------------------------------------------

    private async Task SendTenantListAsync(ConnectedAdmin admin, CancellationToken ct)
    {
        var tenants = await _tenantStore.GetAllAsync();

        var infos = tenants.Select(t =>
        {
            var onlineCount = _tenantHosts.TryGetValue(t.TenantId, out var pool) ? pool.Count : 0;
            return new TenantInfoMessage(
                t.TenantId, t.DisplayName, t.AdminEmail,
                onlineCount, onlineCount, // TotalNodeCount will come from DB later
                t.IsActive, t.CreatedAt,
                t.LicenseKey, t.AdminToken);
        }).ToArray();

        var msg = new TenantListMessage(infos);
        await MessageFraming.WriteFrameAsync(admin.Stream, MessageType.TenantList,
            JsonSerializer.SerializeToUtf8Bytes(msg), ct);
    }

    private async Task HandleCreateTenantAsync(ConnectedAdmin admin, CreateTenantMessage req, CancellationToken ct)
    {
        try
        {
            var tenant = await _tenantStore.CreateTenantAsync(req.DisplayName, req.AdminEmail);
            var ack = new CreateTenantAckMessage(
                true, null,
                tenant.TenantId, tenant.LicenseKey, tenant.AdminToken);
            await MessageFraming.WriteFrameAsync(admin.Stream, MessageType.CreateTenantAck,
                JsonSerializer.SerializeToUtf8Bytes(ack), ct);
        }
        catch (Exception ex)
        {
            var ack = new CreateTenantAckMessage(false, ex.Message, null, null, null);
            await MessageFraming.WriteFrameAsync(admin.Stream, MessageType.CreateTenantAck,
                JsonSerializer.SerializeToUtf8Bytes(ack), ct);
        }
    }

    // -------------------------------------------------------------------------
    // ADMIN NOTIFICATIONS
    // -------------------------------------------------------------------------

    private async Task NotifyAdminHostOnlineAsync(ConnectedHost host, Tenant tenant, CancellationToken ct)
    {
        var msg = new HostOnlineMessage(host.MachineId, host.MachineName, tenant.TenantId);
        await NotifyAdminsAsync(host.TenantId, MessageType.HostOnline,
            JsonSerializer.SerializeToUtf8Bytes(msg), ct);
    }

    private async Task NotifyAdminHostOfflineAsync(ConnectedHost host, Tenant tenant, CancellationToken ct)
    {
        var msg = new HostOfflineMessage(host.MachineId, host.MachineName);
        await NotifyAdminsAsync(host.TenantId, MessageType.HostOffline,
            JsonSerializer.SerializeToUtf8Bytes(msg), ct);
    }

    /// <summary>
    /// Notify the tenant's own admin AND the super admin (if connected).
    /// </summary>
    private async Task NotifyAdminsAsync(string tenantId, MessageType type, byte[] payload, CancellationToken ct)
    {
        // Notify the tenant's own admin
        if (_admins.TryGetValue(tenantId, out var tenantAdmin) &&
            tenantAdmin.WebSocket.State == WebSocketState.Open)
        {
            try { await MessageFraming.WriteFrameAsync(tenantAdmin.Stream, type, payload, ct); }
            catch { /* best effort */ }
        }

        // Notify the super admin too (if connected and different from tenant admin)
        if (_admins.TryGetValue(SuperAdminKey, out var superAdmin) &&
            superAdmin.WebSocket.State == WebSocketState.Open)
        {
            try { await MessageFraming.WriteFrameAsync(superAdmin.Stream, type, payload, ct); }
            catch { /* best effort */ }
        }
    }
}

public sealed class ConnectedHost
{
    public required string MachineId { get; init; }
    public required string MachineName { get; init; }
    public required string OsVersion { get; init; }
    public required string TenantId { get; init; }
    public required WebSocket WebSocket { get; init; }
    public required WebSocketStreamAdapter Stream { get; init; }
    public DateTime ConnectedAt { get; init; }
    public DateTime LastHeartbeat { get; set; } = DateTime.UtcNow;
}

public sealed class ConnectedAdmin
{
    public required WebSocket WebSocket { get; init; }
    public required WebSocketStreamAdapter Stream { get; init; }
    public required string TenantId { get; init; }   // "*" for super admin
    public required bool IsSuperAdmin { get; init; }
}
