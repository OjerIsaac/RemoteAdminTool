using System.Management;
using System.Security.Cryptography;
using System.Text;

namespace RemoteAdminTool.Host;

/// <summary>
/// Derives a stable, hardware-bound MachineId that cannot be spoofed by copying a file.
///
/// Components used (all queried via WMI):
///   - CPU ProcessorId  (unique per physical CPU)
///   - Motherboard SerialNumber
///   - Primary network adapter MAC address (physical adapters only)
///
/// The final ID is the first 16 hex characters of SHA-256(joined components).
/// Falls back gracefully if WMI is unavailable (locked-down/virtual machines).
/// </summary>
public static class HardwareFingerprint
{
    private const int IdLength = 16; // hex chars = 8 bytes of SHA-256

    /// <summary>
    /// Computes a hardware-derived MachineId.
    /// Returns a 16-char hex string that is stable across reboots and OS reinstalls
    /// but unique per physical machine.
    /// </summary>
    public static Task<string> ComputeAsync()
    {
        // WMI calls are synchronous - run off the thread pool so we don't block startup
        return Task.Run(() =>
        {
            try
            {
                var parts = new List<string>();

                // CPU ID
                using (var searcher = new ManagementObjectSearcher("SELECT ProcessorId FROM Win32_Processor"))
                foreach (var obj in searcher.Get())
                {
                    var val = obj["ProcessorId"]?.ToString()?.Trim();
                    if (!string.IsNullOrEmpty(val)) parts.Add(val);
                }

                // Motherboard serial
                using (var searcher = new ManagementObjectSearcher("SELECT SerialNumber FROM Win32_BaseBoard"))
                foreach (var obj in searcher.Get())
                {
                    var val = obj["SerialNumber"]?.ToString()?.Trim();
                    if (!string.IsNullOrEmpty(val) && val != "To be filled by O.E.M.") parts.Add(val);
                }

                // Primary physical MAC address
                using (var searcher = new ManagementObjectSearcher(
                    "SELECT MACAddress, PhysicalAdapter FROM Win32_NetworkAdapter WHERE PhysicalAdapter=TRUE"))
                foreach (var obj in searcher.Get())
                {
                    var val = obj["MACAddress"]?.ToString()?.Trim();
                    if (!string.IsNullOrEmpty(val)) { parts.Add(val); break; } // first one only
                }

                if (parts.Count == 0)
                    return FallbackId();

                var combined = string.Join("|", parts);
                var hash     = SHA256.HashData(Encoding.UTF8.GetBytes(combined));
                return Convert.ToHexString(hash)[..IdLength].ToLowerInvariant();
            }
            catch
            {
                return FallbackId();
            }
        });
    }

    /// <summary>
    /// Fallback for VMs or locked-down machines where WMI is unavailable.
    /// Uses MachineName + UserName - not hardware-bound, but better than a random GUID.
    /// </summary>
    private static string FallbackId()
    {
        var seed  = $"{Environment.MachineName}|{Environment.UserName}";
        var hash  = SHA256.HashData(Encoding.UTF8.GetBytes(seed));
        return "fb" + Convert.ToHexString(hash)[..14].ToLowerInvariant(); // "fb" prefix = fallback
    }
}
