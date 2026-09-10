using Microsoft.EntityFrameworkCore;

namespace RemoteAdminTool.RelayServer;

/// <summary>
/// EF Core database context for the relay server.
/// Manages the Tenants table in MySQL.
/// </summary>
public sealed class RelayDbContext : DbContext
{
    public RelayDbContext(DbContextOptions<RelayDbContext> options) : base(options) { }

    public DbSet<Tenant> Tenants => Set<Tenant>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Tenant>(entity =>
        {
            entity.HasKey(t => t.TenantId);
            entity.HasIndex(t => t.LicenseKey).IsUnique();
            entity.HasIndex(t => t.AdminToken).IsUnique();
        });
    }
}
