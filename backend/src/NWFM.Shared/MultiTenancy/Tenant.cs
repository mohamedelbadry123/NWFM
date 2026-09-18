namespace NWFM.Shared.MultiTenancy;

public sealed class Tenant
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
}
