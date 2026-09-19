namespace Workflow.Application.Integrations;

using System.Text.Json;
using NWFM.Shared.Results;

public sealed record ConnectionInput(string Name, string Kind, string Address, string Authentication,
    Dictionary<string, string>? Credentials = null, bool AllowPrivateNetwork = false, int Port = 587, bool UseTls = true);
public sealed record ConnectionDto(Guid Id, string Name, string Kind, string Address, string Authentication,
    bool HasCredentials, bool AllowPrivateNetwork, int Port, bool UseTls);
public sealed record EventEnvelope(string EventId, string EventKey, string CorrelationId, JsonElement Payload);
public sealed record IntegrationResult(bool Success, int? StatusCode, string Body, string? Error,
    bool Retryable = false, bool TimedOut = false, Dictionary<string, string>? Headers = null);
public sealed record OperationDto(Guid Id, Guid WorkflowInstanceId, Guid ActivityInstanceId, string Kind,
    string Status, int Attempts, DateTime NextAttemptAt, string? Error, int? StatusCode, DateTime CreatedAt);
public sealed record EventReceiptDto(Guid Id, string EventId, string EventKey, string CorrelationId, string Status, Guid? ActivityInstanceId, string? Error, DateTime CreatedAt);
public sealed record EventWaitDto(Guid Id, Guid WorkflowInstanceId, Guid ActivityInstanceId, string EventKey, string CorrelationId, string Status, DateTime ExpiresAt);

public sealed class HttpActivityConfiguration
{
    public Guid ConnectionId { get; set; }
    public string Method { get; set; } = "GET";
    public string Path { get; set; } = "/";
    public Dictionary<string, string> Headers { get; set; } = [];
    public Dictionary<string, string> Query { get; set; } = [];
    public string? Body { get; set; }
    public string ContentType { get; set; } = "application/json";
    public int[] SuccessStatusCodes { get; set; } = [];
    public Dictionary<string, string> OutputMappings { get; set; } = [];
    public int TimeoutSeconds { get; set; } = 30;
    public int MaxAttempts { get; set; } = 1;
    public int RetryDelaySeconds { get; set; } = 10;
    public string IdempotencyHeader { get; set; } = "Idempotency-Key";
    public string ErrorOutcome { get; set; } = "error";
    public string TimeoutOutcome { get; set; } = "timeout";
}

public sealed class EventActivityConfiguration
{
    public Guid ConnectionId { get; set; }
    public string EventKey { get; set; } = "";
    public string CorrelationVariable { get; set; } = "CorrelationId";
    public Dictionary<string, string> OutputMappings { get; set; } = [];
    public int TimeoutSeconds { get; set; } = 86400;
    public string TimeoutOutcome { get; set; } = "timeout";
}

public sealed class EmailActivityConfiguration
{
    public Guid ConnectionId { get; set; }
    public string Channels { get; set; } = "InApp";
    public Guid[] RecipientUserIds { get; set; } = [];
    public string To { get; set; } = "";
    public string Cc { get; set; } = "";
    public string Bcc { get; set; } = "";
    public string Subject { get; set; } = "Workflow notification";
    public string Body { get; set; } = "";
    public bool IsHtml { get; set; }
    public string TemplateKey { get; set; } = "workflow.default";
    public string FailurePolicy { get; set; } = "FailWorkflow";
    public int MaxAttempts { get; set; } = 3;
    public int RetryDelaySeconds { get; set; } = 30;
}

public static class IntegrationJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    public static T Read<T>(string? json) where T : new() => string.IsNullOrWhiteSpace(json)
        ? new T() : JsonSerializer.Deserialize<T>(json, Options) ?? new T();
}

public interface IWorkflowIntegrations
{
    Task<IReadOnlyList<Workflow.Application.DTOs.WorkflowValidationIssueDto>> ValidateConnectionsAsync(Workflow.Application.Models.WorkflowXmlDocument document, CancellationToken ct);
    Task<IReadOnlyList<ConnectionDto>> ListConnectionsAsync(CancellationToken ct);
    Task<Result<ConnectionDto>> SaveConnectionAsync(Guid? id, ConnectionInput input, CancellationToken ct);
    Task<Result> DeleteConnectionAsync(Guid id, CancellationToken ct);
    Task<Result<IntegrationResult>> TestHttpAsync(HttpActivityConfiguration configuration, Dictionary<string, JsonElement> variables, CancellationToken ct);
    Task<Result<Guid>> ReceiveEventAsync(Guid connectionId, string rawBody, string? timestamp, string? signature, string? apiKey, CancellationToken ct);
    Task<IReadOnlyList<OperationDto>> ListOperationsAsync(Guid? instanceId, CancellationToken ct);
    Task<Result> ReplayOperationAsync(Guid operationId, CancellationToken ct);
    Task<IReadOnlyList<EventReceiptDto>> ListEventsAsync(CancellationToken ct);
    Task<IReadOnlyList<EventWaitDto>> ListWaitsAsync(CancellationToken ct);
    Task<Result> ReplayEventAsync(Guid receiptId, Guid? activityId, CancellationToken ct);
}

public interface IWorkflowIntegrationRuntime
{
    Task<Result> QueueAsync(Guid instanceId, Guid activityId, string kind, string configurationJson,
        IReadOnlyDictionary<string, object?> variables, string operationKey, CancellationToken ct);
    Task<Result> RegisterWaitAsync(Guid instanceId, Guid activityId, string configurationJson,
        IReadOnlyDictionary<string, object?> variables, CancellationToken ct);
}
