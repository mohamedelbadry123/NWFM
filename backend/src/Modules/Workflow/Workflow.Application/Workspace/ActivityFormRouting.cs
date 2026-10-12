using System.Text.Json;

namespace Workflow.Application.Workspace;

public sealed class ActivityFormStep
{
    public string Id { get; set; } = "";
    public Guid FormId { get; set; }
    public int VersionNo { get; set; }
    public string Name { get; set; } = "";
    public bool ReviewRequired { get; set; }
    public List<FormRoutingRule> Rules { get; set; } = [];
    public FormRouteDestination Otherwise { get; set; } = new();
}

public sealed class FormRoutingRule
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public FormConditionGroup When { get; set; } = new();
    public FormRouteDestination Destination { get; set; } = new();
}

public sealed class FormConditionGroup
{
    public string Match { get; set; } = "All";
    public List<FormRoutingCondition> Conditions { get; set; } = [];
    public List<FormConditionGroup> Groups { get; set; } = [];
}

public sealed class FormRoutingCondition
{
    public string Field { get; set; } = "";
    public string Operator { get; set; } = "equal";
    public JsonElement Value { get; set; } = JsonSerializer.SerializeToElement<object?>(null);
    public JsonElement To { get; set; } = JsonSerializer.SerializeToElement<object?>(null);
}

public sealed class FormRouteDestination
{
    // Next follows the current order, and follows the existing forward arrow on the last form.
    public string Kind { get; set; } = "Next";
    public string? Target { get; set; }
}

/// <summary>One state per work item. Visit identities make returns distinct from retries.</summary>
public sealed class ActivityFormProgress
{
    public string ActiveStepId { get; set; } = "";
    public Guid VisitId { get; set; }
    public string? ExitNodeKey { get; set; }
    public List<ActivityFormVisit> Visits { get; set; } = [];
    public static ActivityFormProgress Start(IReadOnlyList<ActivityFormStep> steps, Guid workItemId)
        => new() { ActiveStepId = steps[0].Id, VisitId = workItemId };
    public static ActivityFormProgress Read(string? json, IReadOnlyList<ActivityFormStep> steps, Guid workItemId)
        => string.IsNullOrWhiteSpace(json) ? Start(steps, workItemId) : JsonSerializer.Deserialize<ActivityFormProgress>(json)!;
}

public sealed class ActivityFormVisit
{
    public Guid VisitId { get; set; }
    public string StepId { get; set; } = "";
    public Guid FormId { get; set; }
    public int VersionNo { get; set; }
    public Guid SubmissionId { get; set; }
    public Guid ActorId { get; set; }
    public DateTime SubmittedAt { get; set; }
    public string? RuleId { get; set; }
    public FormRouteDestination Destination { get; set; } = new();
    public Dictionary<string, object?> Answers { get; set; } = [];
}
