using NWFM.Shared.Results;

namespace NWFM.Shared.Integration.Forms;

/// <summary>Owning modules validate server-owned context before any immutable submission or replay.</summary>
public interface IFormSubmissionGuard
{
    Task<Result> ValidateAsync(Guid formId, int? version, string? contextType, string? contextId,
        Guid? clientSubmissionId, IReadOnlyDictionary<string, object?> answers, string? actorId, CancellationToken ct);
}
