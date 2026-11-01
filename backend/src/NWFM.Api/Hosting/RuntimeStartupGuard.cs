using NWFM.Shared.Options;

namespace NWFM.Api.Hosting;

/// <summary>
/// The tenant API runs with each tenant's restricted runtime credential and must never be asked to
/// migrate, seed or install SQL objects: those need the owner credential the control plane holds.
/// </summary>
public static class RuntimeStartupGuard
{
    public static void RefuseDatabaseInitialization(DatabaseStartupOptions startup)
    {
        if (startup.InitializeOnly || startup.ApplyMigrations || startup.SeedData || startup.ApplySqlObjects)
            throw new InvalidOperationException(
                "The tenant API never initializes databases. Run NWFM.Platform.Api, which migrates the catalog and every tenant database.");
    }
}
