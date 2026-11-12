using NWFM.Shared.MultiTenancy;

namespace Workflow.Domain.Entities;

/// <summary>Mutable execution-local data. A fork copies its parent's snapshot into each branch.</summary>
public sealed class WorkflowRoutingScope : ITenantAware
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid WorkflowInstanceId { get; set; }
    public string ValuesJson { get; set; } = "{}";
    public string ChangedNodesJson { get; set; } = "[]";
    public byte[] RowVersion { get; set; } = [];
}

/// <summary>Immutable result of a particular activity visit; retries cannot replace another visit.</summary>
public sealed class WorkflowRoutingResult : ITenantAware
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid WorkflowInstanceId { get; set; }
    public Guid ActivityInstanceId { get; set; }
    public Guid? ExecutionTokenId { get; set; }
    public string NodeKey { get; set; } = "";
    public string Outcome { get; set; } = "";
    public string ValuesJson { get; set; } = "{}";
    public DateTime CreatedAt { get; set; }
}

/// <summary>One durable coordination gate per fork visit, independent of previous loop cycles.</summary>
public sealed class WorkflowRoutingActivation : ITenantAware
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid WorkflowInstanceId { get; set; }
    public Guid WorkflowVersionId { get; set; }
    public Guid? ParentTokenId { get; set; }
    public string ForkNodeKey { get; set; } = "";
    public string JoinNodeKey { get; set; } = "";
    public string StatesJson { get; set; } = "{}";
    public string SkippedJson { get; set; } = "[]";
    public string Status { get; set; } = "Waiting";
    public Guid? WinnerTokenId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? DeadlineAt { get; set; }
    public DateTime? ReleasedAt { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

public sealed class WorkflowRoutingUpgradeSnapshot : ITenantAware
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid WorkflowVersionId { get; set; }
    public string Xml { get; set; } = "";
    public string? DesignerJson { get; set; }
    public string? WorkspaceJson { get; set; }
    public DateTime CreatedAt { get; set; }
}
