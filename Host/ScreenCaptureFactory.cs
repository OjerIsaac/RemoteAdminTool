namespace RemoteAdminTool.Host;

/// <summary>
/// Creates the best available screen capture implementation.
/// Tries DXGI Desktop Duplication first (fastest), falls back to GDI.
/// </summary>
public static class ScreenCaptureFactory
{
    public static IScreenCapture Create()
    {
        try
        {
            var dxgi = new DxgiScreenCapture();
            // Test if it actually works (will fail under RDP, etc.)
            dxgi.GetScreenSize();
            Console.WriteLine("[ScreenCapture] Using DXGI Desktop Duplication (GPU-accelerated)");
            return dxgi;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ScreenCapture] DXGI unavailable ({ex.Message}), falling back to GDI");
            return new ScreenCapture();
        }
    }
}
