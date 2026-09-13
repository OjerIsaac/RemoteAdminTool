using System.ComponentModel.DataAnnotations;

namespace RemoteAdminTool.RelayServer;

/// <summary>
/// EF Core entity representing one sub-admin tenant.
/// Stored in the PostgreSQL database on the relay server.
/// </summary>
public sealed class Tenant
{
    [Key]
    [MaxLength(64)]
    public string TenantId { get; set; } = string.Empty;

    [MaxLength(256)]
    [Required]
    public string DisplayName { get; set; } = string.Empty;

    [MaxLength(320)]
    public string AdminEmail { get; set; } = string.Empty;

    /// <summary>
    /// The raw license key shown in the Viewer UI and used for building host.key.
    /// Stored plain-text in DB for display and rebuild purposes.
    /// The hashed version (LicenseKeyHash) is what Host.exe actually sends.
    /// </summary>
    [MaxLength(64)]
    [Required]
    public string LicenseKey { get; set; } = string.Empty;

    /// <summary>
    /// SHA-256 hash of LicenseKey (hex, lowercase). This is what is written to
    /// host.key and what the Host sends to the relay for validation.
    /// </summary>
    [MaxLength(64)]
    [Required]
    public string LicenseKeyHash { get; set; } = string.Empty;

    /// <summary>
    /// Token the sub-admin enters in the Viewer app to authenticate.
    /// </summary>
    [MaxLength(64)]
    [Required]
    public string AdminToken { get; set; } = string.Empty;

    /// <summary>
    /// Per-tenant host auth token — baked into this tenant's Host.exe only.
    /// Prevents a sub-admin who reverse-engineers their EXE from registering
    /// machines under another tenant's LicenseKey.
    /// </summary>
    [MaxLength(64)]
    [Required]
    public string HostToken { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Maximum number of distinct machines (MachineIds) allowed to register
    /// under this tenant. 0 = unlimited (default until billing is set up).
    /// </summary>
    public int MaxHosts { get; set; } = 0;

    /// <summary>Known/approved machines for this tenant (tracked in DB).</summary>
    public ICollection<RegisteredHost> KnownHosts { get; set; } = new List<RegisteredHost>();
}
