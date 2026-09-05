namespace RemoteAdminTool.Host;

/// <summary>
/// Abstraction for screen capture. Allows swapping between GDI (reliable fallback)
/// and DXGI Desktop Duplication (GPU-accelerated, much faster).
/// </summary>
public interface IScreenCapture : IDisposable
{
    /// <summary>Returns the primary screen dimensions.</summary>
    (int Width, int Height) GetScreenSize();

    /// <summary>Returns the dimensions of all available monitors.</summary>
    MonitorInfo[] GetMonitors();

    /// <summary>Sets which monitor to capture (index into GetMonitors array).</summary>
    void SelectMonitor(int monitorIndex);

    /// <summary>Captures the current screen as a JPEG byte array.</summary>
    byte[] CaptureFrameAsJpeg(long quality = 70);
}

public record MonitorInfo(int Index, string Name, int Width, int Height, bool IsPrimary);
