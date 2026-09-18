namespace Workflow.Application.Helpers;

public static class WorkflowOutcomeKeys
{
    public const string Redirect = "REDIRECT";

    public static bool IsRedirect(string? outcomeKey, string? resultValue = null)
        => Matches(outcomeKey) || Matches(resultValue);

    private static bool Matches(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        var normalized = value.Trim().Replace('-', '_');
        return string.Equals(normalized, Redirect, StringComparison.OrdinalIgnoreCase);
    }
}
