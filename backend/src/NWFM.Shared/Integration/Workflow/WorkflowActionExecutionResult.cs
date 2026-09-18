namespace NWFM.Shared.Integration.Workflow;

public sealed record WorkflowActionExecutionResult(
    bool Success,
    IReadOnlyDictionary<string, object?> OutputVariables,
    string? ErrorCode = null,
    string? ErrorMessage = null,
    bool IsRetryable = false)
{
    public static WorkflowActionExecutionResult Succeeded(
        IReadOnlyDictionary<string, object?>? outputVariables = null)
        => new(true, outputVariables ?? new Dictionary<string, object?>());

    public static WorkflowActionExecutionResult Failed(
        string errorCode,
        string errorMessage,
        bool isRetryable = false)
        => new(false, new Dictionary<string, object?>(), errorCode, errorMessage, isRetryable);
}
