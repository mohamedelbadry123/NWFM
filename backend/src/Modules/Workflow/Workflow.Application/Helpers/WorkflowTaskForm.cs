namespace Workflow.Application.Helpers;

using System.Text.Json;
using System.Text.RegularExpressions;
using Workflow.Application.Integrations;

public sealed record WorkflowTaskField(string Key, string LabelEn, string LabelAr, string Type, bool Required, string[]? Options = null);
public sealed class WorkflowTaskConfiguration
{
    public string? InstructionsEn { get; set; }
    public string? InstructionsAr { get; set; }
    public string? FallbackAssignmentKey { get; set; }
    public List<WorkflowTaskField> FormFields { get; set; } = [];
    public string? InputMappingJson { get; set; }
    public string? OutputMappingJson { get; set; }
}
public static class WorkflowTaskForm
{
    public static WorkflowTaskConfiguration Parse(string? json) => IntegrationJson.Read<WorkflowTaskConfiguration>(json);
    public static Dictionary<string, string> Mappings(string? json) => IntegrationJson.Read<Dictionary<string, string>>(json);
    public static string? Validate(WorkflowTaskConfiguration config, IReadOnlyDictionary<string, JsonElement> values)
    {
        if (values.Keys.Any(k => config.FormFields.All(f => f.Key != k))) return "The form contains an unknown field.";
        foreach (var field in config.FormFields)
        {
            if (!values.TryGetValue(field.Key, out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined
                || value.ValueKind == JsonValueKind.String && string.IsNullOrWhiteSpace(value.GetString()))
            { if (field.Required) return $"Field '{field.LabelEn ?? field.Key}' is required."; continue; }
            var valid = field.Type switch
            {
                "number" => value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out _),
                "checkbox" or "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
                "date" => value.ValueKind == JsonValueKind.String && DateOnly.TryParseExact(value.GetString(), "yyyy-MM-dd", out _),
                "email" => value.ValueKind == JsonValueKind.String && System.Net.Mail.MailAddress.TryCreate(value.GetString(), out _),
                "select" => value.ValueKind == JsonValueKind.String && field.Options?.Contains(value.GetString()) == true,
                _ => value.ValueKind == JsonValueKind.String && value.GetString()!.Length <= 10000
            };
            if (!valid) return $"Field '{field.LabelEn ?? field.Key}' has an invalid {field.Type} value.";
        }
        return null;
    }
    public static string? ValidateConfiguration(WorkflowTaskConfiguration config)
    {
        if (config.FormFields is null || config.FormFields.Any(f => f is null || string.IsNullOrWhiteSpace(f.Key) || !Regex.IsMatch(f.Key, @"^[A-Za-z_][A-Za-z0-9_]*$")
            || !new[] { "text", "textarea", "number", "date", "email", "checkbox", "boolean", "select" }.Contains(f.Type))) return "Form fields need a valid key and supported type.";
        if (config.FormFields.Any(f => f.Type == "select" && (f.Options is null || f.Options.Length == 0))) return "Select fields need at least one option.";
        if (config.FormFields.Select(f => f.Key).Distinct().Count() != config.FormFields.Count) return "Form field keys must be unique.";
        try { Mappings(config.InputMappingJson); Mappings(config.OutputMappingJson); }
        catch (JsonException) { return "Input and output mappings must contain a JSON object of destination keys and source paths."; }
        return null;
    }
}
