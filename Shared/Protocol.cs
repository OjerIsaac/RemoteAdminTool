namespace RemoteAdminTool.Shared;

// ---- Message envelope ----
// Every message on the wire starts with a 4-byte length prefix (int32, little-endian)
// followed by a 1-byte MessageType, followed by the payload for that type.
// This keeps framing simple over a raw TCP/TLS stream.

public enum MessageType : byte
{
    Hello = 1,          // Viewer -> Host: handshake + auth token
    HelloAck = 2,        // Host -> Viewer: accept/reject
    FrameData = 3,        // Host -> Viewer: a screen frame (compressed image bytes)
    MouseMove = 4,        // Viewer -> Host
    MouseButton = 5,      // Viewer -> Host
    MouseWheel = 6,        // Viewer -> Host
    KeyEvent = 7,          // Viewer -> Host
    ClipboardSync = 8,      // either direction
    Disconnect = 9,        // either direction
    Heartbeat = 10,         // either direction, keep-alive

    // ---- Relay / hub-and-spoke messages ----
    HostRegister = 20,      // Host -> Relay: "I'm online, here's my info"
    HostRegisterAck = 21,   // Relay -> Host: registration accepted/rejected
    HostList = 22,          // Relay -> Admin: list of online hosts
    RequestHostList = 23,   // Admin -> Relay: "send me the current host list"
    ConnectToHost = 24,     // Admin -> Relay: "connect me to host X"
    SessionStarted = 25,    // Relay -> Admin + Host: session is established
    DisconnectFromHost = 26, // Admin -> Relay: end current session
    HostOffline = 27,       // Relay -> Admin: a host went offline
    HostOnline = 28,        // Relay -> Admin: a host came online
    AdminAuth = 29,         // Admin -> Relay: authenticate as admin
    AdminAuthAck = 30,      // Relay -> Admin: auth accepted/rejected

    // ---- Feature messages (Phase 5) ----
    FileTransferRequest = 40,   // either direction: request to send a file
    FileTransferAccept = 41,    // either direction: accept file transfer
    FileTransferData = 42,      // either direction: chunk of file data
    FileTransferComplete = 43,  // either direction: file transfer done
    FileTransferCancel = 44,    // either direction: cancel transfer
    ChatMessage = 50,           // either direction: text chat
    MonitorList = 60,           // Host -> Admin: list of available monitors
    MonitorSelect = 61,         // Admin -> Host: select which monitor to capture

    // ---- Multi-tenant / super-admin messages ----
    TenantList = 70,            // SuperAdmin -> Relay: request all tenants; Relay -> SuperAdmin: list of tenants
    TenantInfo = 71,            // Relay -> SuperAdmin: info about a single tenant
    CreateTenant = 72,          // SuperAdmin -> Relay: create a new sub-admin tenant
    CreateTenantAck = 73,       // Relay -> SuperAdmin: tenant created / failed
    SuspendTenant = 74,         // SuperAdmin -> Relay: suspend a tenant (no new host connections)
    ResumeTenant = 75,          // SuperAdmin -> Relay: re-activate a suspended tenant

    // ---- Remote Console (PowerShell) ----
    ShellInput  = 80,           // Viewer -> Host: a line of text to send to the shell's stdin
    ShellOutput = 81,           // Host -> Viewer: a chunk of stdout/stderr from the shell
    ShellStarted = 82,          // Host -> Viewer: shell process is ready
    ShellEnded  = 83,           // Host -> Viewer: shell process exited

    // ---- System Monitor ----
    StatsRequest = 90,          // Viewer -> Host: start (Enable=true) or stop (Enable=false) streaming stats
    SystemStats  = 91,          // Host -> Viewer: live CPU, RAM, process, disk, network snapshot
}

public readonly record struct FrameHeader(
    int Width,
    int Height,
    long TimestampTicks,
    int PayloadLength
);

public readonly record struct MouseMoveMessage(int X, int Y);

public enum MouseButtonAction : byte { Down = 0, Up = 1 }
public enum MouseButtonKind : byte { Left = 0, Right = 1, Middle = 2 }

public readonly record struct MouseButtonMessage(
    MouseButtonKind Button,
    MouseButtonAction Action,
    int X,
    int Y
);

public readonly record struct MouseWheelMessage(int DeltaY, int X, int Y);

public enum KeyAction : byte { Down = 0, Up = 1 }

public readonly record struct KeyEventMessage(
    int VirtualKeyCode,
    KeyAction Action
);

public readonly record struct HelloMessage(
    string ClientVersion,
    string AuthToken
);

public readonly record struct HelloAckMessage(
    bool Accepted,
    string? RejectReason,
    int ScreenWidth,
    int ScreenHeight
);

// ---- Relay model messages ----

/// <summary>
/// Sent by a Host agent when it connects to the relay.
/// LicenseKey identifies which sub-admin tenant this host belongs to.
/// </summary>
public readonly record struct HostRegisterMessage(
    string MachineId,
    string MachineName,
    string OsVersion,
    string HostVersion,
    string AuthToken,
    string LicenseKey    // tenant identifier baked into the Host installer
);

public readonly record struct HostRegisterAckMessage(
    bool Accepted,
    string? RejectReason
);

public readonly record struct HostInfoMessage(
    string MachineId,
    string MachineName,
    string OsVersion,
    bool IsOnline,
    DateTime LastSeen,
    string TenantId     // included so super admin can see which tenant owns the host
);

public readonly record struct HostListMessage(
    HostInfoMessage[] Hosts
);

public readonly record struct RequestHostListMessage();

public readonly record struct ConnectToHostMessage(string MachineId);

public readonly record struct SessionStartedMessage(
    string MachineId,
    string MachineName,
    int ScreenWidth,
    int ScreenHeight
);

public readonly record struct DisconnectFromHostMessage(string MachineId);

public readonly record struct HostOfflineMessage(string MachineId, string MachineName);

public readonly record struct HostOnlineMessage(string MachineId, string MachineName, string TenantId);

/// <summary>
/// Sent by a viewer (sub-admin or super admin) to authenticate with the relay.
/// - Sub-admins: set LicenseKey to their tenant's license key, IsSuperAdmin = false.
/// - Super admin: set IsSuperAdmin = true, LicenseKey can be empty/null.
/// </summary>
public readonly record struct AdminAuthMessage(
    string AuthToken,
    string? LicenseKey,
    bool IsSuperAdmin
);

public readonly record struct AdminAuthAckMessage(
    bool Accepted,
    string? RejectReason,
    bool IsSuperAdmin       // echoed back so the viewer UI knows which mode to show
);

// ---- File transfer messages ----

public readonly record struct FileTransferRequestMessage(
    string TransferId,
    string FileName,
    long FileSize,
    string? DestinationPath
);

public readonly record struct FileTransferAcceptMessage(string TransferId);

public readonly record struct FileTransferDataMessage(
    string TransferId,
    int ChunkIndex,
    int TotalChunks,
    byte[] Data
);

public readonly record struct FileTransferCompleteMessage(
    string TransferId,
    string Checksum
);

public readonly record struct FileTransferCancelMessage(
    string TransferId,
    string? Reason
);

// ---- Chat ----

public readonly record struct ChatMessageData(
    string SenderName,
    string Text,
    DateTime TimestampUtc
);

// ---- Monitor selection ----

public readonly record struct MonitorListMessage(
    MonitorInfoData[] Monitors
);

public readonly record struct MonitorInfoData(
    int Index,
    string Name,
    int Width,
    int Height,
    bool IsPrimary
);

public readonly record struct MonitorSelectMessage(int MonitorIndex);

// ---- Multi-tenant messages ----

/// <summary>
/// A lightweight summary of one sub-admin tenant, sent in the tenant list to the super admin.
/// LicenseKey and AdminToken are included so super admin can view/share them at any time.
/// </summary>
public readonly record struct TenantInfoMessage(
    string TenantId,
    string DisplayName,
    string AdminEmail,
    int OnlineNodeCount,
    int TotalNodeCount,
    bool IsActive,
    DateTime CreatedAt,
    string LicenseKey,   // shown in super admin detail view; also needed for BuildHostForSubAdmin.bat
    string AdminToken,   // shown in super admin detail view
    string HostToken     // per-tenant; baked into Host.exe - also shown in detail view
);

public readonly record struct TenantListMessage(
    TenantInfoMessage[] Tenants
);

/// <summary>
/// Super admin sends this to create a new sub-admin tenant.
/// The relay generates a LicenseKey and AdminToken, returns them in CreateTenantAck.
/// </summary>
public readonly record struct CreateTenantMessage(
    string DisplayName,
    string AdminEmail
);

public readonly record struct CreateTenantAckMessage(
    bool Success,
    string? Error,
    string? TenantId,
    string? LicenseKey,     // embed this into the Host installer for this sub-admin
    string? AdminToken,     // sub-admin uses this to log into the Viewer
    string? HostToken       // per-tenant host auth token; baked into Host.exe
);

public readonly record struct SuspendTenantMessage(string TenantId);
public readonly record struct ResumeTenantMessage(string TenantId);

// ---- Remote Console messages ----

/// <summary>Viewer sends a line of text to the remote PowerShell stdin.</summary>
public readonly record struct ShellInputMessage(string Input);

/// <summary>Host streams a chunk of stdout or stderr back to the viewer.</summary>
public readonly record struct ShellOutputMessage(string Text, bool IsError);

// ShellStarted and ShellEnded carry no payload - empty byte[] is used.

// ---- System Monitor messages ----

/// <summary>Viewer asks the Host to start or stop sending SystemStats.</summary>
public readonly record struct StatsRequestMessage(bool Enable);

/// <summary>A snapshot of system health sent by the Host every ~2 seconds.</summary>
public readonly record struct SystemStatsMessage(
    double CpuPercent,
    long RamUsedMb,
    long RamTotalMb,
    double DiskReadMbps,
    double DiskWriteMbps,
    double NetSentMbps,
    double NetRecvMbps,
    ProcessInfoData[] TopProcesses
);

public readonly record struct ProcessInfoData(
    int Pid,
    string Name,
    double CpuPercent,
    long RamMb
);
