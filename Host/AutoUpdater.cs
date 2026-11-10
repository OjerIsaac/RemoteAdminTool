using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Serilog;

namespace RemoteAdminTool.Host;

/// <summary>
/// Checks the relay's /api/version endpoint on startup and periodically while idle.
/// If a newer Host version is available, verifies the SHA-256 hash of the download,
/// then replaces this EXE and relaunches.
/// Returns true if an update was applied (caller should exit immediately after).
/// </summary>
public static class AutoUpdater
{
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30) };

    /// <summary>
    /// Call this at startup or when idle.
    /// Returns true if the process is being replaced - caller must exit immediately.
    /// </summary>
    public static async Task<bool> CheckAndUpdateAsync(string relayHttpUrl, string currentVersion, string authToken)
    {
        try
        {
            Log.Information("[Updater] Checking for updates (current: v{CurrentVersion})...", currentVersion);

            var versionUrl = $"{relayHttpUrl.TrimEnd('/')}/api/version";

            using var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, versionUrl);
            request.Headers.TryAddWithoutValidation("X-Host-Token", authToken);

            using var response = await _http.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                Log.Warning("[Updater] Version check returned {StatusCode} - skipping.", (int)response.StatusCode);
                return false;
            }

            var json = await response.Content.ReadAsStringAsync();
            var doc  = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("host",    out var hostVerEl) ||
                !doc.RootElement.TryGetProperty("hostUrl", out var hostUrlEl))
            {
                Log.Warning("[Updater] Version endpoint missing expected fields - skipping.");
                return false;
            }

            var latestVersion = hostVerEl.GetString() ?? currentVersion;
            var downloadUrl   = hostUrlEl.GetString() ?? "";
            var expectedHash  = doc.RootElement.TryGetProperty("hostSha256", out var sha) ? sha.GetString() ?? "" : "";

            if (string.IsNullOrWhiteSpace(downloadUrl))
            {
                Log.Information("[Updater] No download URL configured - skipping.");
                return false;
            }

            if (!IsNewer(latestVersion, currentVersion))
            {
                Log.Information("[Updater] Up to date (v{CurrentVersion}).", currentVersion);
                return false;
            }

            Log.Information("[Updater] New version available: v{LatestVersion}. Downloading...", latestVersion);
            return await ApplyUpdateAsync(downloadUrl, expectedHash);
        }
        catch (Exception ex)
        {
            // Never crash the app over a failed update check
            Log.Warning("[Updater] Update check failed: {Message}", ex.Message);
            return false;
        }
    }

    private static async Task<bool> ApplyUpdateAsync(string downloadUrl, string expectedSha256)
    {
        var exePath = Environment.ProcessPath
                      ?? System.Diagnostics.Process.GetCurrentProcess().MainModule!.FileName!;
        var exeDir  = Path.GetDirectoryName(exePath)!;
        var newPath = Path.Combine(exeDir, "Host.exe.new");

        var bytes = await _http.GetByteArrayAsync(downloadUrl);
        Log.Information("[Updater] Downloaded {SizeKb:N0} KB.", bytes.Length / 1024);

        // Verify SHA-256 hash before applying (prevents MITM injection)
        if (!string.IsNullOrWhiteSpace(expectedSha256))
        {
            var actualHash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            if (!string.Equals(actualHash, expectedSha256.Trim().ToLowerInvariant(), StringComparison.Ordinal))
            {
                Log.Warning("[Updater] Hash mismatch! Expected {Expected}, got {Actual}. Aborting update.", expectedSha256, actualHash);
                return false;
            }
            Log.Information("[Updater] Hash verified OK.");
        }

        await File.WriteAllBytesAsync(newPath, bytes);

        // Hidden PowerShell process retries replacing the EXE until file lock is released, then relaunches
        var script = $"for ($i = 0; $i -lt 15; $i++) {{ Start-Sleep -Seconds 1; try {{ Move-Item -Force -LiteralPath '{newPath}' -Destination '{exePath}' -ErrorAction Stop; break }} catch {{ }} }}; Start-Process -FilePath '{exePath}'";
        var psi = new ProcessStartInfo
        {
            FileName        = "powershell.exe",
            Arguments       = $"-NoProfile -WindowStyle Hidden -NonInteractive -Command \"{script}\"",
            CreateNoWindow  = true,
            UseShellExecute = false,
            WindowStyle     = ProcessWindowStyle.Hidden
        };
        Process.Start(psi);
        Log.Information("[Updater] Update applied - relaunching...");
        return true;
    }

    private static bool IsNewer(string candidate, string current)
    {
        if (Version.TryParse(candidate, out var cand) &&
            Version.TryParse(current,   out var cur))
            return cand > cur;
        return string.Compare(candidate, current, StringComparison.Ordinal) > 0;
    }
}
