namespace Workflow.Domain.Entities;

using NWFM.Shared.Domain;
using NWFM.Shared.MultiTenancy;

public sealed class WorkflowIntegrationConnection : Entity, ITenantAware
{
    public Guid OrganizationId { get; private set; }
    public string Name { get; private set; } = "";
    public string Kind { get; private set; } = "Http";
    public string Address { get; private set; } = "";
    public string Authentication { get; private set; } = "None";
    public string ProtectedCredentials { get; private set; } = "";
    public bool AllowPrivateNetwork { get; private set; }
    public int Port { get; private set; } = 587;
    public bool UseTls { get; private set; } = true;
    public byte[] RowVersion { get; private set; } = [];
    private WorkflowIntegrationConnection() { }
    public static WorkflowIntegrationConnection Create(Guid organizationId) => new()
    { Id = Guid.NewGuid(), OrganizationId = organizationId, CreatedAt = DateTime.UtcNow };
    public void Update(string name, string kind, string address, string authentication, string? protectedCredentials,
        bool allowPrivateNetwork, int port, bool useTls)
    {
        Name = name; Kind = kind; Address = address; Authentication = authentication;
        if (protectedCredentials is not null) ProtectedCredentials = protectedCredentials;
        AllowPrivateNetwork = allowPrivateNetwork; Port = port; UseTls = useTls;
        SetUpdated(DateTime.UtcNow);
    }
}
