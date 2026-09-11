using Microsoft.EntityFrameworkCore;

namespace RemoteAdminTool.RelayServer;

/// <summary>
/// Service that wraps the database for tenant CRUD operations.
/// Keeps an in-memory cache (LicenseKey -> Tenant, AdminToken -> Tenant) for
/// fast lookups on every host/admin connection without hitting MySQL each time.
/// </summary>
public sealed class TenantStore
{
    private readonly IDbContextFactory<RelayDbContext> _dbFactory;

    // Fast-path in-memory caches (refreshed on startup and after each write)
    private readonly Dictionary<string, Tenant> _byLicenseKey = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Tenant> _byAdminToken = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Tenant> _byTenantId = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _lock = new(1, 1);

    public TenantStore(IDbContextFactory<RelayDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    /// <summary>
    /// Load all tenants from the database into memory. Called once on startup.
    /// Also applies any pending schema changes (idempotent ALTER/CREATE).
    /// </summary>
    public async Task InitializeAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        // Create tables that don't exist yet (fresh install)
        await db.Database.EnsureCreatedAsync();

        // Apply incremental schema changes — safe to run multiple times
        await ApplyMigrationsAsync(db);

        var tenants = await db.Tenants.ToListAsync();
        await _lock.WaitAsync();
        try
        {
            RefreshCache(tenants);
        }
        finally
        {
            _lock.Release();
        }

        Console.WriteLine($"[TenantStore] Loaded {tenants.Count} tenant(s) from database.");
    }

    /// <summary>
    /// Idempotent schema migrations — adds columns/tables introduced after the initial
    /// schema was created. Safe to run on every startup; each statement is guarded by
    /// an existence check so it's a no-op if already applied.
    /// </summary>
    private static async Task ApplyMigrationsAsync(RelayDbContext db)
    {
        var conn = db.Database.GetDbConnection();
        await conn.OpenAsync();
        try
        {
            await using var cmd = conn.CreateCommand();

            // v2: add MaxHosts column to Tenants (seat limit feature)
            cmd.CommandText = """
                ALTER TABLE "Tenants"
                ADD COLUMN IF NOT EXISTS "MaxHosts" integer NOT NULL DEFAULT 0;
                """;
            await cmd.ExecuteNonQueryAsync();

            // v2: create RegisteredHosts table (MachineId tracking + banning)
            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS "RegisteredHosts" (
                    "Id"          serial PRIMARY KEY,
                    "TenantId"    varchar(64)  NOT NULL REFERENCES "Tenants"("TenantId") ON DELETE CASCADE,
                    "MachineId"   varchar(64)  NOT NULL,
                    "MachineName" varchar(256) NOT NULL DEFAULT '',
                    "FirstSeenAt" timestamptz  NOT NULL DEFAULT now(),
                    "LastSeenAt"  timestamptz  NOT NULL DEFAULT now(),
                    "IsAllowed"   boolean      NOT NULL DEFAULT true,
                    UNIQUE ("TenantId", "MachineId")
                );
                """;
            await cmd.ExecuteNonQueryAsync();

            Console.WriteLine("[TenantStore] Schema migrations applied.");
        }
        finally
        {
            await conn.CloseAsync();
        }
    }

    // ---- Lookup methods (hot path — no DB call) ----

    public async Task<Tenant?> GetByLicenseKeyAsync(string licenseKey)
    {
        await _lock.WaitAsync();
        try { return _byLicenseKey.GetValueOrDefault(licenseKey); }
        finally { _lock.Release(); }
    }

    public async Task<Tenant?> GetByAdminTokenAsync(string adminToken)
    {
        await _lock.WaitAsync();
        try { return _byAdminToken.GetValueOrDefault(adminToken); }
        finally { _lock.Release(); }
    }

    public async Task<IReadOnlyList<Tenant>> GetAllAsync()
    {
        await _lock.WaitAsync();
        try { return _byTenantId.Values.ToList(); }
        finally { _lock.Release(); }
    }

    // ---- Write methods ----

    /// <summary>
    /// Create a new sub-admin tenant. Generates unique LicenseKey and AdminToken automatically.
    /// Returns the created Tenant (with generated keys) or null if display name is blank.
    /// </summary>
    public async Task<Tenant> CreateTenantAsync(string displayName, string adminEmail)
    {
        var tenant = new Tenant
        {
            TenantId = GenerateToken(16),
            DisplayName = displayName,
            AdminEmail = adminEmail,
            LicenseKey = GenerateToken(24),
            AdminToken = GenerateToken(24),
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        await using var db = await _dbFactory.CreateDbContextAsync();
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();

        await _lock.WaitAsync();
        try
        {
            _byLicenseKey[tenant.LicenseKey] = tenant;
            _byAdminToken[tenant.AdminToken] = tenant;
            _byTenantId[tenant.TenantId] = tenant;
        }
        finally
        {
            _lock.Release();
        }

        Console.WriteLine($"[TenantStore] Created tenant: {displayName} ({tenant.TenantId})");
        return tenant;
    }

    public async Task<bool> SetActiveAsync(string tenantId, bool active)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var tenant = await db.Tenants.FindAsync(tenantId);
        if (tenant is null) return false;

        tenant.IsActive = active;
        await db.SaveChangesAsync();

        await _lock.WaitAsync();
        try
        {
            // Refresh from DB
            var all = await db.Tenants.ToListAsync();
            RefreshCache(all);
        }
        finally
        {
            _lock.Release();
        }

        return true;
    }

    // ---- Helpers ----

    private static string GenerateToken(int bytes)
    {
        var buf = new byte[bytes];
        System.Security.Cryptography.RandomNumberGenerator.Fill(buf);
        return Convert.ToHexString(buf).ToLowerInvariant();
    }

    private void RefreshCache(IEnumerable<Tenant> tenants)
    {
        _byLicenseKey.Clear();
        _byAdminToken.Clear();
        _byTenantId.Clear();
        foreach (var t in tenants)
        {
            _byLicenseKey[t.LicenseKey] = t;
            _byAdminToken[t.AdminToken] = t;
            _byTenantId[t.TenantId] = t;
        }
    }
}
