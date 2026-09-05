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
    Heartbeat = 10          // either direction, keep-alive
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
