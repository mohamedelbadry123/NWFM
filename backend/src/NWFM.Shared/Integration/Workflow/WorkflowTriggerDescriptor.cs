namespace NWFM.Shared.Integration.Workflow;

/// <summary>
/// Describes a business-module trigger that can be bound to a workflow definition.
/// </summary>
public sealed record WorkflowTriggerDescriptor(
    string EventKey,
    string NameEn,
    string NameAr,
    string? ScreenKey = null);
