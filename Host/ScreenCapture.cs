using System.Drawing;
using System.Drawing.Imaging;

namespace RemoteAdminTool.Host;

/// <summary>
/// Basic GDI-based screen capture. Simple and reliable, but not the fastest option.
/// For higher frame rates later, this can be replaced with the DXGI Desktop Duplication
/// API (via SharpDX or Vortice.Windows) without changing the rest of the pipeline --
/// it just needs to keep producing JPEG/PNG byte arrays.
/// </summary>
public sealed class ScreenCapture
{
    public (int Width, int Height) GetScreenSize()
    {
        var bounds = System.Windows.Forms.Screen.PrimaryScreen!.Bounds;
        return (bounds.Width, bounds.Height);
    }

    public byte[] CaptureFrameAsJpeg(long quality = 70)
    {
        var bounds = System.Windows.Forms.Screen.PrimaryScreen!.Bounds;

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

    private static ImageCodecInfo GetJpegEncoder()
    {
        return ImageCodecInfo.GetImageEncoders()
            .First(codec => codec.FormatID == ImageFormat.Jpeg.Guid);
    }
}
