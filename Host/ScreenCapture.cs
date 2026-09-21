using System.Drawing;
using System.Drawing.Imaging;

namespace RemoteAdminTool.Host;

/// <summary>
/// GDI-based screen capture - simple, reliable fallback that works everywhere.
/// For higher frame rates, use DxgiScreenCapture when available.
/// Now supports multi-monitor selection.
/// </summary>
public sealed class ScreenCapture : IScreenCapture
{
    private int _selectedMonitor = -1; // -1 = primary

    public (int Width, int Height) GetScreenSize()
    {
        var screen = GetSelectedScreen();
        return (screen.Bounds.Width, screen.Bounds.Height);
    }

    public MonitorInfo[] GetMonitors()
    {
        var screens = System.Windows.Forms.Screen.AllScreens;
        var monitors = new MonitorInfo[screens.Length];
        for (int i = 0; i < screens.Length; i++)
        {
            monitors[i] = new MonitorInfo(
                i,
                screens[i].DeviceName,
                screens[i].Bounds.Width,
                screens[i].Bounds.Height,
                screens[i].Primary);
        }
        return monitors;
    }

    public void SelectMonitor(int monitorIndex)
    {
        var screens = System.Windows.Forms.Screen.AllScreens;
        if (monitorIndex >= 0 && monitorIndex < screens.Length)
        {
            _selectedMonitor = monitorIndex;
        }
    }

    public byte[] CaptureFrameAsJpeg(long quality = 70)
    {
        var screen = GetSelectedScreen();
        var bounds = screen.Bounds;

        using var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
        }

        using var ms = new MemoryStream();
        var jpegEncoder = GetJpegEncoder();
        var encoderParams = new EncoderParameters(1);
        encoderParams.Param[0] = new EncoderParameter(Encoder.Quality, quality);

        bitmap.Save(ms, jpegEncoder, encoderParams);
        return ms.ToArray();
    }

    private System.Windows.Forms.Screen GetSelectedScreen()
    {
        var screens = System.Windows.Forms.Screen.AllScreens;
        if (_selectedMonitor >= 0 && _selectedMonitor < screens.Length)
        {
            return screens[_selectedMonitor];
        }
        return System.Windows.Forms.Screen.PrimaryScreen!;
    }

    private static ImageCodecInfo GetJpegEncoder()
    {
        return ImageCodecInfo.GetImageEncoders()
            .First(codec => codec.FormatID == ImageFormat.Jpeg.Guid);
    }

    public void Dispose()
    {
        // GDI capture has no persistent resources to clean up
    }
}
