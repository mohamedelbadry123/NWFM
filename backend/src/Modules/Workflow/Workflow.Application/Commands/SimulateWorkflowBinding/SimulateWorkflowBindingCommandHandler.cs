namespace Workflow.Application.Commands.SimulateWorkflowBinding;

using System.Text.Json;
using MediatR;
using NWFM.Shared.Results;
using Workflow.Application.Abstractions;
using Workflow.Application.Constants;
using Workflow.Application.DTOs;
using Workflow.Domain.Entities;
using Workflow.Domain.Enums;
using Workflow.Domain.Repositories;

public sealed class SimulateWorkflowBindingCommandHandler
    : IRequestHandler<SimulateWorkflowBindingCommand, Result<WorkflowSimulationResultDto>>
{
    private const int MaxSteps = 100;

    private readonly IWorkflowFeatureGate _gate;
    private readonly IWorkflowBindingRepository _bindingRepo;
    private readonly IWorkflowVersionResolver _versionResolver;
    private readonly IWorkflowTransitionEvaluator _transitionEvaluator;

    public SimulateWorkflowBindingCommandHandler(
        IWorkflowFeatureGate gate,
        IWorkflowBindingRepository bindingRepo,
        IWorkflowVersionResolver versionResolver,
        IWorkflowTransitionEvaluator transitionEvaluator)
    {
        _gate                 = gate;
        _bindingRepo          = bindingRepo;
        _versionResolver      = versionResolver;
        _transitionEvaluator  = transitionEvaluator;
    }

    public async Task<Result<WorkflowSimulationResultDto>> Handle(
        SimulateWorkflowBindingCommand request,
        CancellationToken cancellationToken)
    {
        var gate = _gate.EnsureEnabled();
        if (gate.IsFailure)
            return Result.Failure<WorkflowSimulationResultDto>(gate.Error);

        // IgnoreQueryFilters via GetByIdForSuperAdminAsync: the catalog simulate endpoint is
        // SuperAdmin-only and the binding belongs to the selected org, not the JWT tenant.
        var binding = await _bindingRepo.GetByIdForSuperAdminAsync(request.BindingId, cancellationToken);
        if (binding is null)
            return Result.Failure<WorkflowSimulationResultDto>(WorkflowErrors.Binding.NotFound);

        if (request.OrganizationId != Guid.Empty && binding.OrganizationId != request.OrganizationId)
            return Result.Failure<WorkflowSimulationResultDto>(WorkflowErrors.Binding.NotFound);

        var versionResult = await _versionResolver.ResolveAsync(binding, cancellationToken);
        if (versionResult.IsFailure)
            return Result.Failure<WorkflowSimulationResultDto>(versionResult.Error);

        var version = versionResult.Value;
        var variables = BuildSampleVariables(request.SamplePayloadJson);
        var steps = new List<WorkflowSimulationStepDto>();
        var warnings = new List<string>();
        var errors = new List<string>();

        var start = version.Activities.FirstOrDefault(a => a.ActivityType == ActivityType.Start);
        if (start is null)
        {
            errors.Add("Version has no Start activity.");
            return Result.Success(new WorkflowSimulationResultDto(steps, warnings, errors));
        }

        var current = start;
        var visited = new HashSet<string>();

        for (var i = 0; i < MaxSteps; i++)
        {
            if (!visited.Add(current.NodeKey))
            {
                errors.Add($"Cycle detected at node '{current.NodeKey}'.");
                break;
            }

            switch (current.ActivityType)
            {
                case ActivityType.Start:
                    steps.Add(new(current.NodeKey, current.ActivityType.ToString(), "Simulation start."));
                    break;

                case ActivityType.UserTask:
                {
                    var assignmentKey = current.AssignmentRules.FirstOrDefault()?.AssignmentKey ?? "(unmapped)";
                    steps.Add(new(current.NodeKey, current.ActivityType.ToString(),
                        $"Would create work item for assignment key '{assignmentKey}'."));
                    break;
                }

                case ActivityType.ExclusiveGateway:
                {
                    var outgoing = version.Transitions
                        .Where(t => t.FromActivityDefinitionId == current.Id).ToList();
                    var key = _transitionEvaluator.Evaluate(outgoing, variables);
                    steps.Add(new(current.NodeKey, current.ActivityType.ToString(),
                        key is null ? "No matching transition." : $"Selected exclusive transition '{key}'."));
                    if (key is null)
                    {
                        errors.Add($"ExclusiveGateway '{current.NodeKey}' had no matching transition.");
                        return Result.Success(new WorkflowSimulationResultDto(steps, warnings, errors));
                    }

                    var selected = outgoing.First(t => t.TransitionKey == key);
                    var next = version.Activities.FirstOrDefault(a => a.Id == selected.ToActivityDefinitionId);
                    if (next is null)
                    {
                        errors.Add($"Transition '{key}' targets a missing activity.");
                        return Result.Success(new WorkflowSimulationResultDto(steps, warnings, errors));
                    }

                    current = next;
                    continue;
                }

                case ActivityType.InclusiveGateway:
                {
                    var outgoing = version.Transitions
                        .Where(t => t.FromActivityDefinitionId == current.Id).ToList();
                    var matched = _transitionEvaluator.EvaluateAllMatching(outgoing, variables);
                    steps.Add(new(current.NodeKey, current.ActivityType.ToString(),
                        matched.Count == 0
                            ? "No matching inclusive branches."
                            : $"Would fork {matched.Count} inclusive branch(es): {string.Join(", ", matched.Select(t => t.TransitionKey))}."));
                    if (matched.Count == 0)
                    {
                        errors.Add($"InclusiveGateway '{current.NodeKey}' had no matching transition.");
                        return Result.Success(new WorkflowSimulationResultDto(steps, warnings, errors));
                    }

                    // Dry-run follows the first matching branch only (parallel spawn not persisted).
                    warnings.Add($"InclusiveGateway '{current.NodeKey}': simulation follows first matching branch only.");
                    var next = version.Activities.FirstOrDefault(a => a.Id == matched[0].ToActivityDefinitionId);
                    if (next is null)
                    {
                        errors.Add("Inclusive branch targets a missing activity.");
                        return Result.Success(new WorkflowSimulationResultDto(steps, warnings, errors));
                    }

                    current = next;
                    continue;
                }

                case ActivityType.ParallelGateway:
                {
                    var outgoing = version.Transitions
                        .Where(t => t.FromActivityDefinitionId == current.Id).ToList();
                    steps.Add(new(current.NodeKey, current.ActivityType.ToString(),
                        $"Would fork {outgoing.Count} parallel branch(es)."));
                    warnings.Add($"ParallelGateway '{current.NodeKey}': simulation follows first outgoing branch only.");
                    if (outgoing.Count == 0)
                    {
                        errors.Add($"ParallelGateway '{current.NodeKey}' has no outgoing transitions.");
                        return Result.Success(new WorkflowSimulationResultDto(steps, warnings, errors));
                    }

                    var next = version.Activities.FirstOrDefault(a => a.Id == outgoing[0].ToActivityDefinitionId);
                    if (next is null)
                    {
                        errors.Add("Parallel branch targets a missing activity.");
                        return Result.Success(new WorkflowSimulationResultDto(steps, warnings, errors));
                    }

                    current = next;
                    continue;
                }

                case ActivityType.CallActivity:
                {
                    var defKey = TryReadDefinitionKey(current.ConfigurationJson) ?? "(missing)";
                    steps.Add(new(current.NodeKey, current.ActivityType.ToString(),
                        $"Would start child definition '{defKey}' and wait for completion."));
                    break;
                }

                case ActivityType.End:
                    steps.Add(new(current.NodeKey, current.ActivityType.ToString(), "Reached End."));
                    return Result.Success(new WorkflowSimulationResultDto(steps, warnings, errors));

                default:
                    steps.Add(new(current.NodeKey, current.ActivityType.ToString(),
                        $"Would execute {current.ActivityType}."));
                    break;
            }

            var nextKey = GetSingleOutgoing(version, current);
            if (nextKey is null)
            {
                errors.Add($"No outgoing transition from '{current.NodeKey}'.");
                break;
            }

            var nextActivity = version.Activities.FirstOrDefault(a => a.NodeKey == nextKey);
            if (nextActivity is null)
            {
                errors.Add($"Outgoing targets missing node '{nextKey}'.");
                break;
            }

            current = nextActivity;
        }

        if (steps.Count >= MaxSteps)
            errors.Add($"Stopped after {MaxSteps} steps (safety limit).");

        return Result.Success(new WorkflowSimulationResultDto(steps, warnings, errors));
    }

    private static IReadOnlyList<WorkflowVariable> BuildSampleVariables(string? samplePayloadJson)
    {
        if (string.IsNullOrWhiteSpace(samplePayloadJson))
            return Array.Empty<WorkflowVariable>();

        try
        {
            using var doc = JsonDocument.Parse(samplePayloadJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return Array.Empty<WorkflowVariable>();

            var list = new List<WorkflowVariable>();
            var now = DateTime.UtcNow;
            var instanceId = Guid.Empty;
            var orgId = Guid.Empty;

            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                var valueJson = prop.Value.ValueKind == JsonValueKind.String
                    ? JsonSerializer.Serialize(prop.Value.GetString())
                    : prop.Value.GetRawText();

                list.Add(WorkflowVariable.Create(
                    orgId, instanceId, prop.Name, VariableDataType.Json, valueJson, now));
            }

            return list;
        }
        catch
        {
            return Array.Empty<WorkflowVariable>();
        }
    }

    private static string? GetSingleOutgoing(WorkflowVersion version, ActivityDefinition from)
    {
        var transition = version.Transitions.FirstOrDefault(t => t.FromActivityDefinitionId == from.Id);
        if (transition is null) return null;
        return version.Activities.FirstOrDefault(a => a.Id == transition.ToActivityDefinitionId)?.NodeKey;
    }

    private static string? TryReadDefinitionKey(string? configurationJson)
    {
        if (string.IsNullOrWhiteSpace(configurationJson)) return null;
        try
        {
            using var doc = JsonDocument.Parse(configurationJson);
            return doc.RootElement.TryGetProperty("definitionKey", out var dk) ? dk.GetString() : null;
        }
        catch { return null; }
    }
}
