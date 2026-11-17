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
    private static readonly ImageCodecInfo _jpegEncoder =
        ImageCodecInfo.GetImageEncoders().First(codec => codec.FormatID == ImageFormat.Jpeg.Guid);

    private int _selectedMonitor = -1; // -1 = primary
    private Bitmap? _reusableBitmap;
    private readonly MemoryStream _reusableStream = new(256 * 1024);
    private readonly object _syncLock = new();

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
        lock (_syncLock)
        {
            var screen = GetSelectedScreen();
            var bounds = screen.Bounds;

            if (_reusableBitmap is null || _reusableBitmap.Width != bounds.Width || _reusableBitmap.Height != bounds.Height)
            {
                _reusableBitmap?.Dispose();
                _reusableBitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format24bppRgb);
            }

            using (var g = Graphics.FromImage(_reusableBitmap))
            {
                g.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
            }

            _reusableStream.Position = 0;
            _reusableStream.SetLength(0);

            using var encoderParams = new EncoderParameters(1);
            using var qualityParam  = new EncoderParameter(Encoder.Quality, quality);
            encoderParams.Param[0]  = qualityParam;

            _reusableBitmap.Save(_reusableStream, _jpegEncoder, encoderParams);
            return _reusableStream.ToArray();
        }
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

    public void Dispose()
    {
        lock (_syncLock)
        {
            _reusableBitmap?.Dispose();
            _reusableBitmap = null;
            _reusableStream.Dispose();
        }
    }
}
