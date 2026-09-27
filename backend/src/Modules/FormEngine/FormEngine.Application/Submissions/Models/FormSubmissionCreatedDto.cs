namespace FormEngine.Application.Submissions.Models;

/// <summary>
/// The outcome of a submit. <see cref="IsReplay"/> marks a retry of a fill already recorded: the
/// original submission is returned rather than a second row, so a client that lost the response to
/// its first attempt can simply send it again.
/// </summary>
public sealed record FormSubmissionCreatedDto(Guid SubmissionId, int VersionNo, bool IsReplay);
