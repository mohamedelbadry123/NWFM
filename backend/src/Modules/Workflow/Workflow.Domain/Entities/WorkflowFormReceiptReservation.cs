namespace Workflow.Domain.Entities;

using NWFM.Shared.MultiTenancy;

/// <summary>Global key ownership only; no form answers. Retained when an attempt is superseded.</summary>
public sealed class WorkflowFormReceiptReservation : ITenantAware
{
    public Guid ReceiptKey { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid WorkItemId { get; private set; }
    private WorkflowFormReceiptReservation() { }
    public static WorkflowFormReceiptReservation Create(Guid key, WorkItem item)
        => new() { ReceiptKey = key, OrganizationId = item.OrganizationId, WorkItemId = item.Id };
}
