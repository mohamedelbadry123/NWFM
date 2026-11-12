namespace Workflow.Application.Mapping;

/// <summary>Freeze criteria once before lookup/wait. A later retry must reuse this object, including the window anchor.</summary>
public static class MappingSelectorPreparation
{
    public static FrozenRelatedCriteria Freeze(RelatedInstanceSelector selector, IReadOnlyList<MappingSample> samples, DateTimeOffset anchor)
    {
        MappingValidation.Policy(selector.Missing);
        if (selector.KeyKind is not ("BusinessKey" or "CorrelationKey") || selector.MultipleMatches is not ("Attention" or "Latest")
            || selector.WindowSeconds is <= 0 or > 31536000 || selector.WorkflowDefinitionId == Guid.Empty || selector.WorkflowVersionId == Guid.Empty)
            throw new FormatException("Invalid related-instance criteria.");
        var key = MappingEvaluator.Text(MappingEvaluator.Evaluate(selector.KeyValue, samples));
        if (string.IsNullOrWhiteSpace(key) || key.Length > 256) throw new FormatException("Related-instance equality key must contain 1 to 256 characters.");
        Guid? starter = null;
        if (selector.StartedByUserId is not null)
        {
            var text = MappingEvaluator.Text(MappingEvaluator.Evaluate(selector.StartedByUserId, samples));
            starter = Guid.TryParse(text, out var id) && id != Guid.Empty ? id : throw new FormatException("StartedByUserId must be a user GUID, not a business customer key.");
        }
        return new(selector.Id, selector.WorkflowDefinitionId, selector.WorkflowVersionId, selector.KeyKind, key, starter, selector.Status,
            anchor.ToUniversalTime(), selector.WindowSeconds is int seconds ? anchor.ToUniversalTime().AddSeconds(-seconds) : null,
            selector.Missing?.Mode == "Wait" ? anchor.ToUniversalTime().AddSeconds(selector.Missing.DeadlineSeconds!.Value) : null,
            selector.MultipleMatches, selector.Missing?.TimeoutOutcome ?? "attention");
    }
}
