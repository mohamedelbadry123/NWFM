namespace Workflow.Domain.Entities;

/// <summary>Versioned business context. Null on legacy definitions for backwards compatibility.</summary>
public sealed record WorkflowWorkspaceDefinition(string Kind, string? ClusterCode = null, string? RegionCode = null, string? CityCode = null);
