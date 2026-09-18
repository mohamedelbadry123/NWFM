namespace Workflow.Application.Models;

using Workflow.Domain.Entities;

public sealed record WorkflowVersionProjection(
    List<ActivityDefinition> Activities,
    List<WorkflowTransition> Transitions,
    List<WorkflowVariableDefinition> Variables,
    List<ActivityAssignmentRule> Rules,
    List<ActivityOutcomeDefinition> Outcomes,
    List<ActivityActionDefinition> Actions);
