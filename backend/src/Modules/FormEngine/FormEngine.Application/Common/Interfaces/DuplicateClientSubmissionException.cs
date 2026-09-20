namespace FormEngine.Application.Common.Interfaces;

/// <summary>
/// The client key on a submission has already been recorded. Thrown by the store when the unique
/// index catches a replay that slipped past the pre-insert check — two retries arriving at once.
/// The handler answers with the original submission, so a replay and a first attempt look the same
/// to the client.
/// </summary>
public sealed class DuplicateClientSubmissionException(Guid clientSubmissionId)
    : Exception($"A submission with client key '{clientSubmissionId}' already exists.")
{
    public Guid ClientSubmissionId { get; } = clientSubmissionId;
}
