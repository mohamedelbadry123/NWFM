using FormEngine.Domain.Constants;
using FormEngine.Domain.Entities;
using NWFM.Shared.Abstractions;
using NWFM.Shared.Constants;

namespace FormEngine.Application.Uploads.Common;

/// <summary>
/// Who may touch an uploaded file. A file id is a bare GUID handed to the client, so possession of
/// one is not permission to read it: while a file is pending it belongs to whoever picked it, and
/// once a submission claims it, seeing it means being allowed to see submissions.
/// </summary>
internal static class FormFileAccess
{
    /// <summary>Whether the caller may remove the file.</summary>
    public static bool CanManage(SubmissionFile file, ICurrentUser user) =>
        IsAdministrator(user) || IsOwner(file, user);

    /// <summary>Whether the caller may download the file.</summary>
    public static bool CanRead(SubmissionFile file, ICurrentUser user)
    {
        if (IsAdministrator(user) || IsOwner(file, user))
        {
            return true;
        }

        // Someone else's pending file is nobody's business. A submitted one is a reviewer's — and, when
        // the fill belongs to a task, whoever can see tasks, since the task's own page shows it.
        if (file.IsPending)
        {
            return false;
        }

        return user.HasPermission(NwfmPolicies.ViewSubmissions)
            || (file.ContextType == FormContextTypes.Task && user.HasPermission(NwfmPolicies.ViewTasks));
    }

    private static bool IsAdministrator(ICurrentUser user) => user.IsInRole(NwfmRoles.Administrator);

    private static bool IsOwner(SubmissionFile file, ICurrentUser user) =>
        file.UploadedBy is not null
        && user.Id is not null
        && string.Equals(file.UploadedBy, user.Id, StringComparison.Ordinal);
}
