using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;

namespace RemoteAdminTool.Viewer;

/// <summary>
/// Checks the relay's /api/version endpoint when the Viewer starts.
/// If a newer Viewer is available, downloads and replaces the EXE, then relaunches.
/// </summary>
public static class ViewerUpdater
{
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30) };

    /// <summary>
    /// Call this once on application startup.
    /// Returns true if an update was applied — caller must shut down the WPF app immediately.
    /// </summary>
    public static async Task<bool> CheckAndUpdateAsync(string relayHttpUrl, string currentVersion)
    {
        try
        {
            var versionUrl = $"{relayHttpUrl.TrimEnd('/')}/api/version";
            var response   = await _http.GetStringAsync(versionUrl);
            var doc        = JsonDocument.Parse(response);

            if (!doc.RootElement.TryGetProperty("viewer", out var verEl) ||
                !doc.RootElement.TryGetProperty("viewerUrl", out var urlEl))
                return false;

            var latestVersion = verEl.GetString() ?? currentVersion;
            var downloadUrl   = urlEl.GetString() ?? "";

            if (string.IsNullOrWhiteSpace(downloadUrl) || !IsNewer(latestVersion, currentVersion))
                return false;

            // Download in background — show a brief toast via the main window if available
            await ApplyUpdateAsync(downloadUrl, latestVersion);
            return true;
        }
        catch
        {
            // Silent — never block the Viewer from starting due to a failed update check
            return false;
        }
    }

    private static async Task ApplyUpdateAsync(string downloadUrl, string newVersion)
    {
        var exePath = Environment.ProcessPath
                      ?? Process.GetCurrentProcess().MainModule!.FileName!;
        var exeDir  = Path.GetDirectoryName(exePath)!;
        var newPath = Path.Combine(exeDir, "Viewer.exe.new");

        var bytes = await _http.GetByteArrayAsync(downloadUrl);
        await File.WriteAllBytesAsync(newPath, bytes);

        var script = $@"
            Start-Sleep -Seconds 2
            Move-Item -Force '{newPath}' '{exePath}'
            Start-Process '{exePath}'
        ".Trim();

        var psi = new ProcessStartInfo
        {
            FileName  = "powershell.exe",
            Arguments = $"-WindowStyle Hidden -NonInteractive -Command \"{script.Replace("\"", "\\\"")}\"",
            CreateNoWindow  = true,
            UseShellExecute = false,
            WindowStyle     = ProcessWindowStyle.Hidden
        };

        Process.Start(psi);
    }

    private static bool IsNewer(string candidate, string current)
    {
        if (Version.TryParse(candidate, out var cand) &&
            Version.TryParse(current,   out var cur))
            return cand > cur;
        return string.Compare(candidate, current, StringComparison.Ordinal) > 0;
    }
}
