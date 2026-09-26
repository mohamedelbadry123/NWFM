using Workflow.Domain.Entities;
using Workflow.Application.DTOs;
using NWFM.Shared.Results;

namespace Workflow.Application.Workspace;

public sealed class BusinessActivityConfiguration
{
    public string? DepartmentCode { get; set; }
    public string? FieldActivityCode { get; set; }
    public string? DefinitionKey { get; set; }
    public Guid? VersionId { get; set; }
    public decimal? SlaDurationHours { get; set; }
    public Guid? SlaPolicyId { get; set; }
    public string? RejectTargetNodeKey { get; set; }
    public List<ActivityEventConfiguration> Events { get; set; } = [];
}

public sealed class ActivityEventConfiguration
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Trigger { get; set; } = "OnApprove";
    public string Kind { get; set; } = "Http";
    private bool? required;
    public bool Required { get => required ?? Kind is "Http" or "Soap"; set => required = value; }
    public System.Text.Json.JsonElement Configuration { get; set; }

    public string DeliveryConfiguration()
    {
        var config = System.Text.Json.Nodes.JsonNode.Parse(Configuration.GetRawText())!.AsObject();
        if (Kind is "Soap" or "Sms" or "Http") config["protocol"] = Kind == "Http" ? "Rest" : Kind;
        if (Kind == "Email") { config["channels"] = "Email"; config["failurePolicy"] = "Retry"; }
        return config.ToJsonString();
    }
}

public interface IWorkflowWorkspacePublisher
{
    Task<IReadOnlyList<WorkflowValidationIssueDto>> ValidateAsync(WorkflowVersion version, CancellationToken ct);
    Task<Result> PreparePublicationAsync(WorkflowVersion version, CancellationToken ct);
}
