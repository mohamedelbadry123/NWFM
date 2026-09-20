namespace Workflow.Domain.Entities;

using NWFM.Shared.Domain;
using NWFM.Shared.MultiTenancy;

public sealed class WorkflowParticipant : Entity, ITenantAware
{
    public Guid OrganizationId { get; private set; }
    public Guid UserId { get; private set; }
    public string? EmployeeNumber { get; private set; }
    public string DisplayName { get; private set; } = string.Empty;
    public string? DisplayNameAr { get; private set; }
    public string Email { get; private set; } = string.Empty;
    public bool IsActive { get; private set; }
    public bool IsDemo { get; private set; }
    public void MarkDemo() => IsDemo = true;
    public byte[] RowVersion { get; private set; } = [];

    private WorkflowParticipant() { }

    public static WorkflowParticipant Create(
        Guid organizationId,
        Guid userId,
        string displayName,
        string email,
        DateTime createdAt,
        string? displayNameAr = null,
        string? employeeNumber = null)
    {
        return new WorkflowParticipant
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            UserId = userId,
            DisplayName = displayName,
            DisplayNameAr = displayNameAr,
            Email = email,
            EmployeeNumber = employeeNumber,
            IsActive = true,
            CreatedAt = createdAt
        };
    }

    public void Update(string displayName, string? displayNameAr, string? employeeNumber, DateTime updatedAt)
    {
        DisplayName = displayName;
        DisplayNameAr = displayNameAr;
        EmployeeNumber = employeeNumber;
        SetUpdated(updatedAt);
    }

    public void Deactivate(DateTime deactivatedAt)
    {
        IsActive = false;
        SetUpdated(deactivatedAt);
    }

    public void Activate(DateTime activatedAt)
    {
        IsActive = true;
        SetUpdated(activatedAt);
    }
}
