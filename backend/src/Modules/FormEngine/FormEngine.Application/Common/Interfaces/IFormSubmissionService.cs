using FormEngine.Application.Submissions.Models;
using NWFM.Shared.Results;

namespace FormEngine.Application.Common.Interfaces;

/// <summary>
/// Records one fill of a form. The single write path: the submit endpoint and the cross-module form
/// gateway both come through here, so validation, replay, media linking and the transaction behave
/// the same whoever asks.
/// </summary>
public interface IFormSubmissionService
{
    Task<Result<FormSubmissionCreatedDto>> SubmitAsync(FormSubmissionRequest request, CancellationToken cancellationToken);
}

/// <summary>One fill to record.</summary>
public sealed record FormSubmissionRequest
{
    public required Guid FormDefinitionId { get; init; }

    /// <summary>The version being answered; null answers the form's current version.</summary>
    public int? VersionNo { get; init; }

    /// <summary>What the fill belongs to — e.g. <c>Task</c> plus the task id. Both or neither.</summary>
    public string? ContextType { get; init; }

    public string? ContextId { get; init; }

    /// <summary>The client's own key; a second send under it answers with the first submission.</summary>
    public Guid? ClientSubmissionId { get; init; }

    /// <summary>When the form was filled, for date rules only. Clamped to the server clock.</summary>
    public DateTimeOffset? ClientFilledAt { get; init; }

    public required IReadOnlyDictionary<string, object?> Answers { get; init; }
}
