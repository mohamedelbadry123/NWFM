using FormEngine.Application.Submissions.Models;
using MediatR;
using NWFM.Shared.Constants;
using NWFM.Shared.Results;
using NWFM.Shared.Security;

namespace FormEngine.Application.Submissions.Commands.SubmitForm;

/// <summary>Records one fill of a form against a published version.</summary>
[Authorize(Policy = NwfmPolicies.SubmitForms)]
public sealed record SubmitFormCommand : IRequest<Result<FormSubmissionCreatedDto>>
{
    public Guid FormDefinitionId { get; init; }

    /// <summary>
    /// The version being answered. Omit it to use the form's current version; a workflow task sends
    /// the version pinned to it, so a redesign cannot change the form under work in flight.
    /// </summary>
    public int? VersionNo { get; init; }

    /// <summary>
    /// What this fill belongs to — e.g. <c>WorkItem</c> plus the work item id. Both or neither.
    /// A stand-alone fill sends neither.
    /// </summary>
    public string? ContextType { get; init; }

    public string? ContextId { get; init; }

    /// <summary>
    /// The client's own key for this fill. Generated once when the form is opened and sent on every
    /// retry: posting twice under one key answers with the first submission rather than filling twice.
    /// </summary>
    public Guid? ClientSubmissionId { get; init; }

    /// <summary>
    /// When the form was actually filled, in local time. Only date rules read it, and only to be fair
    /// to a client that filled offline. Clamped to the server clock, so a client cannot date a fill
    /// forward to walk past a "must not be in the future" rule.
    /// </summary>
    public DateTimeOffset? ClientFilledAt { get; init; }

    /// <summary>Answers keyed by field <c>data_name</c>. Keys the version does not declare are ignored.</summary>
    public Dictionary<string, object?> Answers { get; init; } = [];
}
