namespace Workflow.Domain.Entities;

using NWFM.Shared.Domain;
using NWFM.Shared.MultiTenancy;

/// <summary>
/// Per-organization process template. Each company owns its own drawings;
/// assignment groups on the canvas belong to this same organization.
/// </summary>
public sealed class WorkflowDefinition : Entity, ITenantAware
{
    public Guid OrganizationId { get; private set; }
    public string DefinitionKey { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string? NameAr { get; private set; }
    public string? Description { get; private set; }
    public string? DescriptionAr { get; private set; }
    public bool IsActive { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    private WorkflowDefinition() { }

    public static WorkflowDefinition Create(
        Guid organizationId,
        string definitionKey,
        string name,
        DateTime createdAt,
        string? nameAr = null,
        string? description = null,
        string? descriptionAr = null)
    {
        return new WorkflowDefinition
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            DefinitionKey = definitionKey,
            Name = name,
            NameAr = nameAr,
            Description = description,
            DescriptionAr = descriptionAr,
            IsActive = true,
            CreatedAt = createdAt
        };
    }

    public void Update(
        string name,
        string? nameAr,
        string? description,
        string? descriptionAr,
        DateTime updatedAt)
    {
        Name = name;
        NameAr = nameAr;
        Description = description;
        DescriptionAr = descriptionAr;
        SetUpdated(updatedAt);
    }

    public void Activate(DateTime updatedAt)
    {
        IsActive = true;
        SetUpdated(updatedAt);
    }

    public void Deactivate(DateTime updatedAt)
    {
        IsActive = false;
        SetUpdated(updatedAt);
    }
}
