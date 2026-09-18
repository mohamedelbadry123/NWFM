namespace Workflow.Domain.Entities;

using NWFM.Shared.Domain;
using Workflow.Domain.Enums;

public sealed class WorkflowVersion : Entity
{
    public const string CurrentSchemaVersion = "1.0";

    public Guid WorkflowDefinitionId { get; private set; }
    public int VersionNumber { get; private set; }
    public WorkflowVersionStatus Status { get; private set; }
    public string XmlContent { get; private set; } = string.Empty;
    public string? XmlHash { get; private set; }
    public string SchemaVersion { get; private set; } = CurrentSchemaVersion;
    public string? DesignerJson { get; private set; }
    public WorkflowValidationStatus ValidationStatus { get; private set; }
    public string? ValidationResultJson { get; private set; }
    public string? ChangeSummary { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public Guid? PublishedByUserId { get; private set; }
    public DateTime? PublishedAt { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    private readonly List<ActivityDefinition> _activities = [];
    private readonly List<WorkflowTransition> _transitions = [];
    private readonly List<WorkflowVariableDefinition> _variables = [];
    private readonly List<ActivityOutcomeDefinition> _outcomes = [];
    private readonly List<ActivityActionDefinition> _actions = [];

    public IReadOnlyList<ActivityDefinition> Activities => _activities.AsReadOnly();
    public IReadOnlyList<WorkflowTransition> Transitions => _transitions.AsReadOnly();
    public IReadOnlyList<WorkflowVariableDefinition> Variables => _variables.AsReadOnly();
    public IReadOnlyList<ActivityOutcomeDefinition> Outcomes => _outcomes.AsReadOnly();
    public IReadOnlyList<ActivityActionDefinition> Actions => _actions.AsReadOnly();

    private WorkflowVersion() { }

    public static WorkflowVersion CreateDraft(
        Guid workflowDefinitionId,
        int versionNumber,
        Guid createdByUserId,
        DateTime createdAt,
        string? changeSummary = null)
    {
        return new WorkflowVersion
        {
            Id = Guid.NewGuid(),
            WorkflowDefinitionId = workflowDefinitionId,
            VersionNumber = versionNumber,
            Status = WorkflowVersionStatus.Draft,
            XmlContent = string.Empty,
            SchemaVersion = CurrentSchemaVersion,
            ValidationStatus = WorkflowValidationStatus.NotValidated,
            CreatedByUserId = createdByUserId,
            ChangeSummary = changeSummary,
            CreatedAt = createdAt
        };
    }

    public bool IsDraft => Status == WorkflowVersionStatus.Draft;

    public void UpdateDesignerJson(string designerJson, DateTime updatedAt)
    {
        DesignerJson = designerJson;
        SetUpdated(updatedAt);
    }

    public void UpdateXml(string xmlContent, string xmlHash, DateTime updatedAt)
    {
        XmlContent = xmlContent;
        XmlHash = xmlHash;
        ValidationStatus = WorkflowValidationStatus.NotValidated;
        ValidationResultJson = null;
        SetUpdated(updatedAt);
    }

    public void SetValidationResult(bool isValid, string resultJson, DateTime updatedAt)
    {
        ValidationStatus = isValid ? WorkflowValidationStatus.Valid : WorkflowValidationStatus.Invalid;
        ValidationResultJson = resultJson;
        SetUpdated(updatedAt);
    }

    public void Publish(Guid publishedByUserId, DateTime publishedAt)
    {
        Status = WorkflowVersionStatus.Published;
        PublishedByUserId = publishedByUserId;
        PublishedAt = publishedAt;
        ValidationStatus = WorkflowValidationStatus.Valid;
        SetUpdated(publishedAt);
    }

    public void Retire(DateTime updatedAt)
    {
        Status = WorkflowVersionStatus.Retired;
        SetUpdated(updatedAt);
    }
}
