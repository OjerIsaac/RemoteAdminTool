using System.Runtime.InteropServices;
using RemoteAdminTool.Shared;

namespace RemoteAdminTool.Host;

/// <summary>
/// Injects mouse/keyboard input using the standard Win32 SendInput API.
/// This is the same mechanism legitimate remote-desktop and accessibility tools use --
/// it does not hook, intercept, or hide anything, it just synthesizes input events.
/// </summary>
public sealed class InputInjector
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    private const uint INPUT_MOUSE = 0;
    private const uint INPUT_KEYBOARD = 1;

    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;
    private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
    private const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
    private const uint MOUSEEVENTF_MIDDLEUP = 0x0040;
    private const uint MOUSEEVENTF_WHEEL = 0x0800;

    private const uint KEYEVENTF_KEYUP = 0x0002;

    public void MoveMouse(int x, int y)
    {
        // Absolute positioning is simpler and reliable enough for a single-monitor
        // personal-use tool. SetCursorPos works fine outside of UAC-elevated targets.
        SetCursorPos(x, y);
    }

    public void ClickMouse(MouseButtonKind button, MouseButtonAction action, int x, int y)
    {
        SetCursorPos(x, y);

        var flags = (button, action) switch
        {
            (MouseButtonKind.Left, MouseButtonAction.Down) => MOUSEEVENTF_LEFTDOWN,
            (MouseButtonKind.Left, MouseButtonAction.Up) => MOUSEEVENTF_LEFTUP,
            (MouseButtonKind.Right, MouseButtonAction.Down) => MOUSEEVENTF_RIGHTDOWN,
            (MouseButtonKind.Right, MouseButtonAction.Up) => MOUSEEVENTF_RIGHTUP,
            (MouseButtonKind.Middle, MouseButtonAction.Down) => MOUSEEVENTF_MIDDLEDOWN,
            (MouseButtonKind.Middle, MouseButtonAction.Up) => MOUSEEVENTF_MIDDLEUP,
            _ => 0u
        };

        var input = new INPUT
        {
            type = INPUT_MOUSE,
            U = new InputUnion { mi = new MOUSEINPUT { dwFlags = flags } }
        };

        SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
    }

    public void ScrollMouse(int deltaY)
    {
        var input = new INPUT
        {
            type = INPUT_MOUSE,
            U = new InputUnion { mi = new MOUSEINPUT { dwFlags = MOUSEEVENTF_WHEEL, mouseData = unchecked((uint)deltaY) } }
        };

        SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
    }

    public void SendKey(int virtualKeyCode, KeyAction action)
    {
        var input = new INPUT
        {
            type = INPUT_KEYBOARD,
            U = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = (ushort)virtualKeyCode,
                    dwFlags = action == KeyAction.Up ? KEYEVENTF_KEYUP : 0
                }
            }
        };

        SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
    }
}
