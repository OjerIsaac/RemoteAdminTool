using Microsoft.EntityFrameworkCore;

namespace RemoteAdminTool.RelayServer;

/// <summary>
/// EF Core database context for the relay server.
/// Manages the Tenants table in PostgreSQL (hosted on Render).
/// </summary>
public sealed class RelayDbContext : DbContext
{
    public RelayDbContext(DbContextOptions<RelayDbContext> options) : base(options) { }

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<RegisteredHost> RegisteredHosts => Set<RegisteredHost>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Tenant>(entity =>
        {
            entity.HasKey(t => t.TenantId);
            entity.HasIndex(t => t.LicenseKey).IsUnique();
            entity.HasIndex(t => t.AdminToken).IsUnique();
        });

        modelBuilder.Entity<RegisteredHost>(entity =>
        {
            // A MachineId is unique within a tenant - same machine can't register twice
            entity.HasIndex(r => new { r.TenantId, r.MachineId }).IsUnique();
            entity.HasOne(r => r.Tenant)
                  .WithMany(t => t.KnownHosts)
                  .HasForeignKey(r => r.TenantId)
                  .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
