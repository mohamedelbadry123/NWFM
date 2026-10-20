namespace NWFM.Shared.Options;

/// <summary>
/// A deployment serves one tenant with a dedicated database. This server-owned
/// identity also namespaces shared infrastructure; client selectors cannot change it.
/// </summary>
public sealed class TenantIsolationOptions
{
    public const string SectionName = "Application";
    public Guid TenantId { get; set; }
    public bool IsActive { get; set; } = true;
}
