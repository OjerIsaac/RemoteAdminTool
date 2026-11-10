using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace RemoteAdminTool.Viewer;

/// <summary>
/// Checks the relay's /api/version endpoint when the Viewer starts.
/// If a newer Viewer is available, verifies the SHA-256 hash, then replaces the EXE and relaunches.
/// </summary>
public static class ViewerUpdater
{
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30) };

    /// <summary>
    /// Call this once on application startup.
    /// Returns true if an update was applied - caller must shut down the WPF app immediately.
    /// </summary>
    public static async Task<bool> CheckAndUpdateAsync(string relayHttpUrl, string currentVersion)
    {
        try
        {
            var versionUrl = $"{relayHttpUrl.TrimEnd('/')}/api/version";

            using var request = new HttpRequestMessage(HttpMethod.Get, versionUrl);
            request.Headers.TryAddWithoutValidation("X-Host-Token", ViewerConstants.HostToken);

            using var response = await _http.SendAsync(request);
            if (!response.IsSuccessStatusCode) return false;

            var json = await response.Content.ReadAsStringAsync();
            var doc  = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("viewer",    out var verEl) ||
                !doc.RootElement.TryGetProperty("viewerUrl", out var urlEl))
                return false;

            var latestVersion = verEl.GetString() ?? currentVersion;
            var downloadUrl   = urlEl.GetString() ?? "";
            var expectedHash  = doc.RootElement.TryGetProperty("viewerSha256", out var sha) ? sha.GetString() ?? "" : "";

            if (string.IsNullOrWhiteSpace(downloadUrl) || !IsNewer(latestVersion, currentVersion))
                return false;

            return await ApplyUpdateAsync(downloadUrl, expectedHash);
        }
        catch
        {
            return false;
        }
    }

    private static async Task<bool> ApplyUpdateAsync(string downloadUrl, string expectedSha256)
    {
        var exePath = Environment.ProcessPath
                      ?? Process.GetCurrentProcess().MainModule!.FileName!;
        var exeDir  = Path.GetDirectoryName(exePath)!;
        var newPath = Path.Combine(exeDir, "Viewer.exe.new");

        var bytes = await _http.GetByteArrayAsync(downloadUrl);

        // Verify SHA-256 before applying
        if (!string.IsNullOrWhiteSpace(expectedSha256))
        {
            var actualHash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            if (!string.Equals(actualHash, expectedSha256.Trim().ToLowerInvariant(), StringComparison.Ordinal))
                return false; // Hash mismatch - silently abort
        }

        await File.WriteAllBytesAsync(newPath, bytes);

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
