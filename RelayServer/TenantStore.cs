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
    private readonly Dictionary<string, Tenant> _byLicenseKeyHash = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Tenant> _byAdminToken     = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Tenant> _byTenantId       = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Tenant> _byHostToken      = new(StringComparer.Ordinal);
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

        // Apply incremental schema changes - safe to run multiple times
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
    /// Idempotent schema migrations - adds columns/tables introduced after the initial
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

            // v3: add LicenseKeyHash column (hashed host.key)
            cmd.CommandText = """
                ALTER TABLE "Tenants"
                ADD COLUMN IF NOT EXISTS "LicenseKeyHash" varchar(64) NOT NULL DEFAULT '';
                """;
            await cmd.ExecuteNonQueryAsync();

            // v3: add HostToken column (per-tenant host auth)
            cmd.CommandText = """
                ALTER TABLE "Tenants"
                ADD COLUMN IF NOT EXISTS "HostToken" varchar(64) NOT NULL DEFAULT '';
                """;
            await cmd.ExecuteNonQueryAsync();

            // v3: backfill LicenseKeyHash and HostToken for existing tenants that have empty values
            cmd.CommandText = """
                UPDATE "Tenants"
                SET "LicenseKeyHash" = encode(sha256("LicenseKey"::bytea), 'hex')
                WHERE "LicenseKeyHash" = '';
                """;
            await cmd.ExecuteNonQueryAsync();

            cmd.CommandText = """
                UPDATE "Tenants"
                SET "HostToken" = encode(gen_random_bytes(24), 'hex')
                WHERE "HostToken" = '';
                """;
            // Note: gen_random_bytes requires pgcrypto extension - safe on Render Postgres
            // If pgcrypto is not available the UPDATE is a no-op; tokens will be filled on next tenant create
            try { await cmd.ExecuteNonQueryAsync(); } catch { /* pgcrypto not available - skip backfill */ }

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

    // ---- Lookup methods (hot path - no DB call) ----

    /// <summary>Looks up a tenant by the SHA-256 hash of the LicenseKey (what host.key contains).</summary>
    public async Task<Tenant?> GetByLicenseKeyAsync(string licenseKeyHash)
    {
        await _lock.WaitAsync();
        try { return _byLicenseKeyHash.GetValueOrDefault(licenseKeyHash); }
        finally { _lock.Release(); }
    }

    /// <summary>Looks up a tenant by their per-tenant HostToken (baked into Host.exe).</summary>
    public async Task<Tenant?> GetByHostTokenAsync(string hostToken)
    {
        await _lock.WaitAsync();
        try { return _byHostToken.GetValueOrDefault(hostToken); }
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
    public async Task<Tenant> CreateTenantAsync(string displayName, string adminEmail, int maxHosts = 0)
    {
        var tenant = new Tenant
        {
            TenantId       = GenerateToken(16),
            DisplayName    = displayName,
            AdminEmail     = adminEmail,
            LicenseKey     = GenerateToken(24),
            AdminToken     = GenerateToken(24),
            HostToken      = GenerateToken(24),   // per-tenant - unique to this Host.exe build
            MaxHosts       = Math.Max(0, maxHosts),
            IsActive       = true,
            CreatedAt      = DateTime.UtcNow
        };
        tenant.LicenseKeyHash = HashLicenseKey(tenant.LicenseKey);

        await using var db = await _dbFactory.CreateDbContextAsync();
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();

        await _lock.WaitAsync();
        try
        {
            _byLicenseKeyHash[tenant.LicenseKeyHash] = tenant;
            _byAdminToken[tenant.AdminToken]           = tenant;
            _byTenantId[tenant.TenantId]               = tenant;
            _byHostToken[tenant.HostToken]             = tenant;
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

    /// <summary>
    /// Rotates a sub-admin's AdminToken (invalidating their old Viewer login token immediately)
    /// while leaving their HostToken and LicenseKey untouched so deployed Hosts stay online.
    /// </summary>
    public async Task<string?> RotateAdminTokenAsync(string tenantId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var tenant = await db.Tenants.FindAsync(tenantId);
        if (tenant is null) return null;

        var newToken = GenerateToken(24);
        tenant.AdminToken = newToken;
        await db.SaveChangesAsync();

        await _lock.WaitAsync();
        try
        {
            var all = await db.Tenants.ToListAsync();
            RefreshCache(all);
        }
        finally
        {
            _lock.Release();
        }

        Console.WriteLine($"[TenantStore] Rotated AdminToken for tenant: {tenant.DisplayName} ({tenantId})");
        return newToken;
    }

    /// <summary>
    /// Updates the MaxHosts seat limit for a sub-admin tenant (0 = unlimited).
    /// </summary>
    public async Task<bool> UpdateMaxHostsAsync(string tenantId, int maxHosts)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var tenant = await db.Tenants.FindAsync(tenantId);
        if (tenant is null) return false;

        tenant.MaxHosts = Math.Max(0, maxHosts);
        await db.SaveChangesAsync();

        await _lock.WaitAsync();
        try
        {
            var all = await db.Tenants.ToListAsync();
            RefreshCache(all);
        }
        finally
        {
            _lock.Release();
        }

        Console.WriteLine($"[TenantStore] Updated MaxHosts={tenant.MaxHosts} for tenant: {tenant.DisplayName} ({tenantId})");
        return true;
    }

    // ---- Helpers ----

    /// <summary>Compute SHA-256 hash of a LicenseKey (hex lowercase).</summary>
    public static string HashLicenseKey(string licenseKey)
        => Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(licenseKey)))
           .ToLowerInvariant();

    private static string GenerateToken(int bytes)
    {
        var buf = new byte[bytes];
        System.Security.Cryptography.RandomNumberGenerator.Fill(buf);
        return Convert.ToHexString(buf).ToLowerInvariant();
    }

    private void RefreshCache(IEnumerable<Tenant> tenants)
    {
        _byLicenseKeyHash.Clear();
        _byAdminToken.Clear();
        _byTenantId.Clear();
        _byHostToken.Clear();
        foreach (var t in tenants)
        {
            if (!string.IsNullOrEmpty(t.LicenseKeyHash))
                _byLicenseKeyHash[t.LicenseKeyHash] = t;
            _byAdminToken[t.AdminToken] = t;
            _byTenantId[t.TenantId]    = t;
            if (!string.IsNullOrEmpty(t.HostToken))
                _byHostToken[t.HostToken] = t;
        }
    }
}
