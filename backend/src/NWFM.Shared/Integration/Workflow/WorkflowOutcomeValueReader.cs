namespace NWFM.Shared.Integration.Workflow;

using System.Text.Json;

/// <summary>Parses the small set of trusted values carried from a completed workflow.</summary>
public static class WorkflowOutcomeValueReader
{
    public static string Normalize(string value)
        => value.Trim().Replace('-', '_').ToUpperInvariant() switch
        {
            "APPROVE" => "APPROVED",
            "REJECT" => "REJECTED",
            "RETURN_FOR_CHANGES" => "RETURNED_FOR_CHANGES",
            var normalized => normalized,
        };

    public static Guid RequireBusinessEntityId(WorkflowOutcomeMessage message, string expectedEntityType)
    {
        if (!string.Equals(message.BusinessEntityType, expectedEntityType, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Workflow outcome entity type '{message.BusinessEntityType}' does not match '{expectedEntityType}'.");
        if (!Guid.TryParse(message.BusinessEntityId, out var id) || id == Guid.Empty)
            throw new InvalidOperationException("Workflow outcome BusinessEntityId is not a valid identifier.");
        return id;
    }

    public static Guid RequireActorUserId(WorkflowOutcomeMessage message)
    {
        foreach (var key in new[] { "CompletedByUserId", "ActorUserId", "StartedByUserId" })
        {
            if (TryGetGuid(message.ApprovedOutputs, key, out var id)) return id;
        }
        throw new InvalidOperationException("Workflow outcome does not identify the authorized actor.");
    }

    public static string? GetString(WorkflowOutcomeMessage message, string key)
    {
        if (!message.ApprovedOutputs.TryGetValue(key, out var raw) || raw is null) return null;
        return raw is JsonElement element
            ? element.ValueKind == JsonValueKind.String ? element.GetString() : element.ToString()
            : raw.ToString();
    }

    private static bool TryGetGuid(IReadOnlyDictionary<string, object?> values, string key, out Guid id)
    {
        id = Guid.Empty;
        if (!values.TryGetValue(key, out var raw) || raw is null) return false;
        var text = raw is JsonElement element && element.ValueKind == JsonValueKind.String
            ? element.GetString()
            : raw.ToString()?.Trim('"');
        return Guid.TryParse(text, out id) && id != Guid.Empty;
    }
}
