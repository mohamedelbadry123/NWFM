namespace Tenancy.Domain.Entities;

/// <summary>Status reasons the system sets itself, so it knows which maintenance it may lift on its own.</summary>
public static class TenantStatusReasons
{
    public const string MigrationFailed = "Schema migration failed.";
}
