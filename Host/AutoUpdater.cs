using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace RemoteAdminTool.Host;

/// <summary>
/// Checks the relay's /api/version endpoint on startup.
/// If a newer Host version is available, verifies the SHA-256 hash of the download,
/// then replaces this EXE and relaunches.
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

            using var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, versionUrl);
            request.Headers.TryAddWithoutValidation("X-Host-Token", HostConstants.HostToken);

            using var response = await _http.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"[Updater] Version check returned {(int)response.StatusCode} — skipping.");
                return false;
            }

            var json = await response.Content.ReadAsStringAsync();
            var doc  = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("host",    out var hostVerEl) ||
                !doc.RootElement.TryGetProperty("hostUrl", out var hostUrlEl))
            {
                Console.WriteLine("[Updater] Version endpoint missing expected fields — skipping.");
                return false;
            }

            var latestVersion = hostVerEl.GetString() ?? currentVersion;
            var downloadUrl   = hostUrlEl.GetString() ?? "";
            var expectedHash  = doc.RootElement.TryGetProperty("hostSha256", out var sha) ? sha.GetString() ?? "" : "";

            if (string.IsNullOrWhiteSpace(downloadUrl))
            {
                Console.WriteLine("[Updater] No download URL configured — skipping.");
                return false;
            }

            if (!IsNewer(latestVersion, currentVersion))
            {
                Console.WriteLine($"[Updater] Up to date (v{currentVersion}).");
                return false;
            }

            Console.WriteLine($"[Updater] New version available: v{latestVersion}. Downloading...");
            await ApplyUpdateAsync(downloadUrl, expectedHash);
            return true;
        }
        catch (Exception ex)
        {
            // Never crash the app over a failed update check
            Console.WriteLine($"[Updater] Update check failed: {ex.Message}");
            return false;
        }
    }

    private static async Task ApplyUpdateAsync(string downloadUrl, string expectedSha256)
    {
        var exePath = Environment.ProcessPath
                      ?? System.Diagnostics.Process.GetCurrentProcess().MainModule!.FileName!;
        var exeDir  = Path.GetDirectoryName(exePath)!;
        var newPath = Path.Combine(exeDir, "Host.exe.new");

        var bytes = await _http.GetByteArrayAsync(downloadUrl);
        Console.WriteLine($"[Updater] Downloaded {bytes.Length / 1024:N0} KB.");

        // Verify SHA-256 hash before applying (prevents MITM injection)
        if (!string.IsNullOrWhiteSpace(expectedSha256))
        {
            var actualHash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            if (!string.Equals(actualHash, expectedSha256.ToLowerInvariant(), StringComparison.Ordinal))
            {
                Console.WriteLine($"[Updater] Hash mismatch! Expected {expectedSha256}, got {actualHash}. Aborting update.");
                return;
            }
            Console.WriteLine("[Updater] Hash verified OK.");
        }

        await File.WriteAllBytesAsync(newPath, bytes);

        // Hidden PowerShell process swaps files after this process exits
        var script = $@"Start-Sleep -Seconds 2; Move-Item -Force '{newPath}' '{exePath}'; Start-Process '{exePath}'";
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

    private static bool IsNewer(string candidate, string current)
    {
        if (Version.TryParse(candidate, out var cand) &&
            Version.TryParse(current,   out var cur))
            return cand > cur;
        return string.Compare(candidate, current, StringComparison.Ordinal) > 0;
    }
}
