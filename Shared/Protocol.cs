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
    MonitorSelect = 61          // Admin -> Host: select which monitor to capture
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

public readonly record struct HostRegisterMessage(
    string MachineId,
    string MachineName,
    string OsVersion,
    string HostVersion,
    string AuthToken
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
    DateTime LastSeen
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

public readonly record struct HostOnlineMessage(string MachineId, string MachineName);

public readonly record struct AdminAuthMessage(string AuthToken);

public readonly record struct AdminAuthAckMessage(
    bool Accepted,
    string? RejectReason
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
