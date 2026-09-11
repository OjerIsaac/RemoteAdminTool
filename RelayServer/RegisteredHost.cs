using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RemoteAdminTool.RelayServer;

/// <summary>
/// Tracks machine IDs that have ever successfully registered under a tenant.
/// Used for two purposes:
///   1. Seat counting — prevents rogue hosts from registering new machine IDs
///      beyond the tenant's MaxHosts limit.
///   2. Audit trail — shows all machines that have ever connected.
/// </summary>
public sealed class RegisteredHost
{
    [Key]
    public int Id { get; set; }

    /// <summary>The tenant this machine belongs to.</summary>
    [MaxLength(64)]
    [Required]
    public string TenantId { get; set; } = string.Empty;

    [ForeignKey(nameof(TenantId))]
    public Tenant? Tenant { get; set; }

    /// <summary>
    /// Unique machine identifier sent by the Host agent on registration.
    /// Generated once per install and persisted in the Host's appsettings.json.
    /// </summary>
    [MaxLength(64)]
    [Required]
    public string MachineId { get; set; } = string.Empty;

    /// <summary>Display name of the machine (hostname or override).</summary>
    [MaxLength(256)]
    public string MachineName { get; set; } = string.Empty;

    public DateTime FirstSeenAt { get; set; } = DateTime.UtcNow;

    public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// If false, this machine is blocked from reconnecting (manual ban).
    /// </summary>
    public bool IsAllowed { get; set; } = true;
}
