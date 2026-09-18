namespace Workflow.Domain.Entities;

using NWFM.Shared.Domain;

public sealed class ActivityOutcomeDefinition : Entity
{
    public Guid WorkflowVersionId { get; private set; }
    public Guid ActivityDefinitionId { get; private set; }
    public string OutcomeKey { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string? NameAr { get; private set; }
    public string? Description { get; private set; }
    public string? DescriptionAr { get; private set; }
    public int SortOrder { get; private set; }
    public bool RequiresComment { get; private set; }
    public bool RequiresAttachment { get; private set; }
    public bool IsDefault { get; private set; }
    public bool IsActive { get; private set; }
    public string? ResultValue { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    private ActivityOutcomeDefinition() { }

    public static ActivityOutcomeDefinition Create(
        Guid workflowVersionId,
        Guid activityDefinitionId,
        string outcomeKey,
        string name,
        int sortOrder,
        DateTime createdAt,
        string? nameAr = null,
        string? description = null,
        string? descriptionAr = null,
        bool requiresComment = false,
        bool requiresAttachment = false,
        bool isDefault = false,
        string? resultValue = null)
    {
        return new ActivityOutcomeDefinition
        {
            Id = Guid.NewGuid(),
            WorkflowVersionId = workflowVersionId,
            ActivityDefinitionId = activityDefinitionId,
            OutcomeKey = outcomeKey.ToUpperInvariant(),
            Name = name,
            NameAr = nameAr,
            Description = description,
            DescriptionAr = descriptionAr,
            SortOrder = sortOrder,
            RequiresComment = requiresComment,
            RequiresAttachment = requiresAttachment,
            IsDefault = isDefault,
            IsActive = true,
            ResultValue = resultValue,
            CreatedAt = createdAt
        };
    }
}
