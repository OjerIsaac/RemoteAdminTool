using System.Drawing;
using System.Drawing.Imaging;
using SharpDX;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using Device = SharpDX.Direct3D11.Device;
using MapFlags = SharpDX.Direct3D11.MapFlags;
using Resource = SharpDX.DXGI.Resource;
using ResultCode = SharpDX.DXGI.ResultCode;

namespace RemoteAdminTool.Host;

/// <summary>
/// GPU-accelerated screen capture using DXGI Desktop Duplication API.
/// Much faster than GDI CopyFromScreen - captures only what changed on the desktop
/// and works at GPU speed. Falls back gracefully if Desktop Duplication is unavailable
/// (e.g., running under RDP, locked screen, or older GPU drivers).
/// </summary>
public sealed class DxgiScreenCapture : IScreenCapture
{
    private static readonly ImageCodecInfo _jpegEncoder =
        ImageCodecInfo.GetImageEncoders().First(codec => codec.FormatID == ImageFormat.Jpeg.Guid);

    private Device? _device;
    private OutputDuplication? _duplication;
    private Texture2D? _stagingTexture;
    private Bitmap? _reusableBitmap;
    private readonly MemoryStream _reusableStream = new(256 * 1024);
    private int _width;
    private int _height;
    private int _selectedMonitor;
    private bool _initialized;
    private readonly object _lock = new();

    public (int Width, int Height) GetScreenSize()
    {
        EnsureInitialized();
        return (_width, _height);
    }

    public MonitorInfo[] GetMonitors()
    {
        using var factory = new Factory1();
        var monitors = new List<MonitorInfo>();
        int index = 0;

        foreach (var adapter in factory.Adapters1)
        {
            foreach (var output in adapter.Outputs)
            {
                var desc = output.Description;
                monitors.Add(new MonitorInfo(
                    index++,
                    desc.DeviceName,
                    desc.DesktopBounds.Right - desc.DesktopBounds.Left,
                    desc.DesktopBounds.Bottom - desc.DesktopBounds.Top,
                    index == 1)); // First output is typically primary
                output.Dispose();
            }
            adapter.Dispose();
        }

        return monitors.ToArray();
    }

    public void SelectMonitor(int monitorIndex)
    {
        lock (_lock)
        {
            if (_selectedMonitor != monitorIndex)
            {
                _selectedMonitor = monitorIndex;
                Cleanup();
                _initialized = false;
            }
        }
    }

    public byte[] CaptureFrameAsJpeg(long quality = 70)
    {
        lock (_lock)
        {
            try
            {
                EnsureInitialized();

                if (_duplication is null || _stagingTexture is null || _device is null)
                {
                    return Array.Empty<byte>();
                }

                // Try to acquire the next frame (timeout 100ms)
                var result = _duplication.TryAcquireNextFrame(100, out _, out Resource? screenResource);

                if (result.Code == ResultCode.WaitTimeout.Code)
                {
                    // No new frame - screen hasn't changed. Return an empty array
                    // to signal "no change" (caller can skip sending).
                    screenResource?.Dispose();
                    return Array.Empty<byte>();
                }

                result.CheckError();

                using (screenResource)
                {
                    using var screenTexture = screenResource!.QueryInterface<Texture2D>();
                    _device.ImmediateContext.CopyResource(screenTexture, _stagingTexture);
                }

                _duplication.ReleaseFrame();

                // Map the staging texture to CPU memory and convert to JPEG
                var dataBox = _device.ImmediateContext.MapSubresource(
                    _stagingTexture, 0, MapMode.Read, MapFlags.None);

                try
                {
                    if (_reusableBitmap is null || _reusableBitmap.Width != _width || _reusableBitmap.Height != _height)
                    {
                        _reusableBitmap?.Dispose();
                        _reusableBitmap = new Bitmap(_width, _height, PixelFormat.Format32bppArgb);
                    }

                    var bitmapData = _reusableBitmap.LockBits(
                        new Rectangle(0, 0, _width, _height),
                        ImageLockMode.WriteOnly,
                        PixelFormat.Format32bppArgb);

                    // Copy row by row (source pitch may differ from destination stride)
                    for (int y = 0; y < _height; y++)
                    {
                        Utilities.CopyMemory(
                            bitmapData.Scan0 + y * bitmapData.Stride,
                            dataBox.DataPointer + y * dataBox.RowPitch,
                            _width * 4);
                    }

                    _reusableBitmap.UnlockBits(bitmapData);

                    _reusableStream.Position = 0;
                    _reusableStream.SetLength(0);

                    using var encoderParams = new EncoderParameters(1);
                    using var qualityParam  = new EncoderParameter(Encoder.Quality, quality);
                    encoderParams.Param[0]  = qualityParam;
                    _reusableBitmap.Save(_reusableStream, _jpegEncoder, encoderParams);
                    return _reusableStream.ToArray();
                }
                finally
                {
                    _device.ImmediateContext.UnmapSubresource(_stagingTexture, 0);
                }
            }
            catch (Exception ex)
            {
                // Any DXGI error (AccessLost, DeviceRemoved, hybrid GPU, elevation switch, etc.)
                // Clean up and return empty so caller smoothly falls back to GDI without crashing the session.
                Console.Error.WriteLine($"[DXGI] Capture error ({ex.Message}), falling back / reinitializing");
                Cleanup();
                _initialized = false;
                return Array.Empty<byte>();
            }
        }
    }

    private void EnsureInitialized()
    {
        if (_initialized) return;

        using var factory = new Factory1();
        var adapter = factory.GetAdapter1(0);

        _device = new Device(adapter, DeviceCreationFlags.BgraSupport);

        // Find the target output (monitor)
        var outputs = adapter.Outputs;
        var outputIndex = Math.Min(_selectedMonitor, outputs.Length - 1);
        outputIndex = Math.Max(0, outputIndex);

        var output = outputs[outputIndex];
        var output1 = output.QueryInterface<Output1>();

        var bounds = output.Description.DesktopBounds;
        _width = bounds.Right - bounds.Left;
        _height = bounds.Bottom - bounds.Top;

        // Create a staging texture (CPU-readable copy of the desktop)
        var textureDesc = new Texture2DDescription
        {
            Width = _width,
            Height = _height,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Staging,
            CpuAccessFlags = CpuAccessFlags.Read,
            BindFlags = BindFlags.None,
            OptionFlags = ResourceOptionFlags.None
        };

        _stagingTexture = new Texture2D(_device, textureDesc);
        _duplication = output1.DuplicateOutput(_device);

        output.Dispose();
        output1.Dispose();
        adapter.Dispose();

        _initialized = true;
        Console.WriteLine($"[DXGI] Initialized: {_width}x{_height} on monitor {outputIndex}");
    }

    private void Cleanup()
    {
        _reusableBitmap?.Dispose();
        _reusableBitmap = null;
        _duplication?.Dispose();
        _duplication = null;
        _stagingTexture?.Dispose();
        _stagingTexture = null;
        _device?.Dispose();
        _device = null;
    }

    public void Dispose()
    {
        Cleanup();
    }

    private static ImageCodecInfo GetJpegEncoder()
    {
        return ImageCodecInfo.GetImageEncoders()
            .First(codec => codec.FormatID == ImageFormat.Jpeg.Guid);
    }
}
