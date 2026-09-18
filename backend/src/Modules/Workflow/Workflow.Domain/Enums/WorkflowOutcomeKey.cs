namespace Workflow.Domain.Enums;

/// <summary>
/// Canonical outcome keys published when a workflow instance reaches a terminal business result.
/// Stored and transmitted as strings for forward-compatible module adapters.
/// </summary>
public static class WorkflowOutcomeKey
{
    public const string Approved = "Approved";
    public const string Rejected = "Rejected";
    public const string ReturnedForChanges = "ReturnedForChanges";
    public const string Completed = "Completed";
    public const string Cancelled = "Cancelled";
    public const string Failed = "Failed";
}
