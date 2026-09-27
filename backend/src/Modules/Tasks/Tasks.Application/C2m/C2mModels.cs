using System.Text.Json.Serialization;

namespace Tasks.Application.C2m;

/// <summary>
/// The closure C2M accepts for a field activity. Property names are C2M's and serialized verbatim —
/// the inconsistent casing (<c>FAId</c>, <c>sourceApp</c>, <c>ParametersList</c>) mirrors what C2M
/// already accepts from WFM, and each is pinned so a serializer naming policy cannot break it.
/// </summary>
public sealed record C2mClosureRequest
{
    [JsonPropertyName("FADetails")]
    public required C2mFaDetails FaDetails { get; init; }

    [JsonPropertyName("sourceApp")]
    public required string SourceApp { get; init; }

    /// <summary>The FA id again; C2M correlates its response on it.</summary>
    [JsonPropertyName("transactionId")]
    public required string TransactionId { get; init; }
}

public sealed record C2mFaDetails
{
    [JsonPropertyName("FAId")]
    public required string FaId { get; init; }

    /// <summary><c>C</c> or <c>X</c>.</summary>
    [JsonPropertyName("FAStatus")]
    public required string FaStatus { get; init; }

    /// <summary>WFM's ticket id for the activity.</summary>
    [JsonPropertyName("MOBId")]
    public string? MobId { get; init; }

    /// <summary>Only when <c>FAStatus</c> is <c>X</c>.</summary>
    [JsonPropertyName("cancelReason")]
    public string? CancelReason { get; init; }

    /// <summary>Only when <c>FAStatus</c> is <c>C</c>.</summary>
    [JsonPropertyName("closureReason")]
    public string? ClosureReason { get; init; }

    [JsonPropertyName("comment")]
    public string? Comment { get; init; }

    /// <summary>Formatted <c>yyyy-MM-dd-HH.mm.ss</c>, as in C2M's samples.</summary>
    [JsonPropertyName("completionDTTM")]
    public string? CompletionDttm { get; init; }

    [JsonPropertyName("imageURL")]
    public string? ImageUrl { get; init; }

    [JsonPropertyName("meterDisconnectionType")]
    public string MeterDisconnectionType { get; init; } = "";

    [JsonPropertyName("ParametersList")]
    public required C2mParametersList ParametersList { get; init; }

    [JsonPropertyName("userId")]
    public required string UserId { get; init; }
}

public sealed record C2mParametersList
{
    [JsonPropertyName("Parameters")]
    public required IReadOnlyList<C2mParameter> Parameters { get; init; }
}

public sealed record C2mParameter
{
    [JsonPropertyName("ParameterName")]
    public required string ParameterName { get; init; }

    [JsonPropertyName("ParameterValue")]
    public required string ParameterValue { get; init; }
}

/// <summary>
/// C2M's answer. <c>status</c> <c>OK</c> with <c>responseCode</c> <c>"0"</c> is an accepted closure;
/// the <c>isSucess</c>/<c>data</c> flags C2M also sends read false on accepted closures, so they are
/// deliberately not modelled.
/// </summary>
public sealed record C2mClosureResponse
{
    [JsonPropertyName("transactionId")]
    public string? TransactionId { get; init; }

    [JsonPropertyName("status")]
    public string? Status { get; init; }

    [JsonPropertyName("responseCode")]
    public string? ResponseCode { get; init; }

    [JsonPropertyName("responseDescription")]
    public string? ResponseDescription { get; init; }

    [JsonPropertyName("errorDescription")]
    public string? ErrorDescription { get; init; }

    [JsonPropertyName("OP_Status")]
    public string? OpStatus { get; init; }

    [JsonPropertyName("TaskCode")]
    public string? TaskCode { get; init; }

    [JsonIgnore]
    public bool IsAccepted =>
        string.Equals(Status, "OK", StringComparison.OrdinalIgnoreCase)
        && string.Equals(ResponseCode, "0", StringComparison.Ordinal);
}

/// <summary>What C2M is told for one Action Taken answer. Exactly one reason is ever set.</summary>
public sealed record C2mActionOutcome(string FaStatus, string? CancelReason, string? ClosureReason);

public enum C2mDispatchOutcomeKind
{
    /// <summary>C2M answered, and accepted the closure.</summary>
    Accepted,

    /// <summary>C2M answered, and refused it. Retrying the same payload will not help.</summary>
    Rejected,

    /// <summary>C2M never answered — timed out, refused the connection, or failed mid-call.</summary>
    TransportFailed,

    /// <summary>Nothing was sent: the integration is off, or closing is bypassed.</summary>
    Skipped,

    /// <summary>An earlier attempt for this task already succeeded, so nothing was sent again.</summary>
    AlreadyAcknowledged,
}

/// <summary>The result of one closure attempt, in the form the caller needs to decide what happens next.</summary>
public sealed record C2mDispatchOutcome
{
    public required C2mDispatchOutcomeKind Kind { get; init; }

    public string? ResponseCode { get; init; }

    public string? Message { get; init; }

    /// <summary>
    /// Whether the task may be approved. A skip counts: switching the integration off, or bypassing the
    /// close during a cutover, must not stop reviewers working.
    /// </summary>
    public bool AllowsApproval => Kind
        is C2mDispatchOutcomeKind.Accepted
        or C2mDispatchOutcomeKind.AlreadyAcknowledged
        or C2mDispatchOutcomeKind.Skipped;

    public static C2mDispatchOutcome Accepted(string? responseCode) =>
        new() { Kind = C2mDispatchOutcomeKind.Accepted, ResponseCode = responseCode };

    public static C2mDispatchOutcome Rejected(string? responseCode, string message) =>
        new() { Kind = C2mDispatchOutcomeKind.Rejected, ResponseCode = responseCode, Message = message };

    public static C2mDispatchOutcome TransportFailed(string message) =>
        new() { Kind = C2mDispatchOutcomeKind.TransportFailed, Message = message };

    public static C2mDispatchOutcome Skipped(string reason) =>
        new() { Kind = C2mDispatchOutcomeKind.Skipped, Message = reason };

    public static C2mDispatchOutcome AlreadyAcknowledged(string? responseCode) =>
        new() { Kind = C2mDispatchOutcomeKind.AlreadyAcknowledged, ResponseCode = responseCode };
}

/// <summary>The C2M closure endpoint, transport-free. Implemented in Infrastructure over HTTP.</summary>
public interface IC2mClient
{
    Task<C2mClosureResponse> CloseFieldActivityAsync(C2mClosureRequest request, CancellationToken cancellationToken);
}
