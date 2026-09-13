using System.Diagnostics;
using System.Text.Json;

namespace RemoteAdminTool.Host;

/// <summary>
/// Checks the relay's /api/version endpoint on startup.
/// If a newer Host version is available, downloads it, replaces this EXE, and relaunches.
/// Returns true if an update was applied (caller should exit immediately after).
/// </summary>
public static class AutoUpdater
{
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30) };

    /// <summary>
    /// Call this once at startup before doing any real work.
    /// Returns true if the process is being replaced — caller must exit immediately.
    /// </summary>
    public static async Task<bool> CheckAndUpdateAsync(string relayHttpUrl, string currentVersion)
    {
        try
        {
            Console.WriteLine($"[Updater] Checking for updates (current: v{currentVersion})...");

            var versionUrl = $"{relayHttpUrl.TrimEnd('/')}/api/version";
            var response   = await _http.GetStringAsync(versionUrl);
            var doc        = JsonDocument.Parse(response);

            if (!doc.RootElement.TryGetProperty("host", out var hostVerEl) ||
                !doc.RootElement.TryGetProperty("hostUrl", out var hostUrlEl))
            {
                Console.WriteLine("[Updater] Version endpoint missing expected fields — skipping update.");
                return false;
            }

            var latestVersion = hostVerEl.GetString() ?? currentVersion;
            var downloadUrl   = hostUrlEl.GetString() ?? "";

            if (string.IsNullOrWhiteSpace(downloadUrl))
            {
                Console.WriteLine("[Updater] No download URL configured — skipping update.");
                return false;
            }

            if (!IsNewer(latestVersion, currentVersion))
            {
                Console.WriteLine($"[Updater] Up to date (v{currentVersion}).");
                return false;
            }

            Console.WriteLine($"[Updater] New version available: v{latestVersion}. Downloading...");
            await ApplyUpdateAsync(downloadUrl);
            return true;
        }
        catch (Exception ex)
        {
            // Never crash the app over a failed update check
            Console.WriteLine($"[Updater] Update check failed: {ex.Message}");
            return false;
        }
    }

    private static async Task ApplyUpdateAsync(string downloadUrl)
    {
        var exePath  = Environment.ProcessPath
                       ?? Process.GetCurrentProcess().MainModule!.FileName!;
        var exeDir   = Path.GetDirectoryName(exePath)!;
        var newPath  = Path.Combine(exeDir, "Host.exe.new");

        // Download new binary
        var bytes = await _http.GetByteArrayAsync(downloadUrl);
        await File.WriteAllBytesAsync(newPath, bytes);
        Console.WriteLine($"[Updater] Downloaded {bytes.Length / 1024:N0} KB.");

        // Use a hidden PowerShell process to swap files after this process exits
        // (Windows won't let you replace a running EXE directly)
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
        Console.WriteLine("[Updater] Update applied — relaunching...");
    }

    /// <summary>Returns true if candidate is a higher version than current.</summary>
    private static bool IsNewer(string candidate, string current)
    {
        if (Version.TryParse(candidate, out var cand) &&
            Version.TryParse(current,   out var cur))
            return cand > cur;

        // Fallback: simple string compare
        return string.Compare(candidate, current, StringComparison.Ordinal) > 0;
    }
}
