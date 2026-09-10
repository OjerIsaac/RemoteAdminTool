using System.ComponentModel.DataAnnotations;

namespace RemoteAdminTool.RelayServer;

/// <summary>
/// EF Core entity representing one sub-admin tenant.
/// Stored in the MySQL database on the relay server.
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
    /// The license key embedded in Host installers for this tenant.
    /// Hosts present this key when connecting to the relay.
    /// </summary>
    [MaxLength(64)]
    [Required]
    public string LicenseKey { get; set; } = string.Empty;

    /// <summary>
    /// Token the sub-admin enters in the Viewer app to authenticate.
    /// </summary>
    [MaxLength(64)]
    [Required]
    public string AdminToken { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
