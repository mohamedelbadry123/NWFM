using NWFM.Shared.Domain;
using NWFM.Shared.Exceptions;
using Tasks.Domain.Constants;

namespace Tasks.Domain.Entities;

/// <summary>
/// What one <c>Action Taken</c> answer tells C2M when a task closes its field activity: the operation
/// status (<c>C</c> or <c>X</c>) and the reason that goes with it. Table: <c>Task.C2mActionMappings</c>.
/// </summary>
/// <remarks>
/// A lookup, not code, because the rule is C2M's: a new cancel reason, or a second code that counts as
/// completed, is reference data changing upstream and must not need a deploy. An option on the form
/// that names its own status wins over this; the built-in rule in <see cref="C2mOperationStatuses"/>
/// covers a code with neither.
/// </remarks>
public sealed class C2mActionMapping : Entity
{
    public const int CodeMaxLength = 50;
    public const int ReasonMaxLength = 50;
    public const int NameMaxLength = 250;
    public const int ActorMaxLength = 256;

    private C2mActionMapping()
    {
    }

    /// <summary>The stored answer on <c>wfm_action_taken</c> — WFM's <c>LKPIDOLD</c> code.</summary>
    public string ActionCode { get; private set; } = default!;

    /// <summary><c>C</c> or <c>X</c> — see <see cref="C2mOperationStatuses"/>.</summary>
    public string FaStatus { get; private set; } = default!;

    /// <summary>Sent only when <see cref="FaStatus"/> is <c>X</c>; C2M rejects it on a completion.</summary>
    public string? CancelReason { get; private set; }

    /// <summary>Sent only when <see cref="FaStatus"/> is <c>C</c>.</summary>
    public string? ClosureReason { get; private set; }

    public string NameEn { get; private set; } = default!;
    public string NameAr { get; private set; } = default!;
    public bool IsActive { get; private set; }
    public string? CreatedBy { get; private set; }
    public string? UpdatedBy { get; private set; }

    public static C2mActionMapping Create(
        string actionCode,
        string faStatus,
        string? cancelReason,
        string? closureReason,
        string nameEn,
        string nameAr,
        bool isActive,
        string? createdBy,
        DateTime utcNow)
    {
        if (string.IsNullOrWhiteSpace(actionCode))
        {
            throw new DomainException("An action mapping must have an action code.");
        }

        var mapping = new C2mActionMapping
        {
            ActionCode = actionCode.Trim().ToUpperInvariant(),
            CreatedBy = Normalize(createdBy),
            CreatedAt = utcNow,
        };

        mapping.Apply(faStatus, cancelReason, closureReason, nameEn, nameAr, isActive);
        mapping.Touch(createdBy, utcNow);
        return mapping;
    }

    public void Update(
        string faStatus,
        string? cancelReason,
        string? closureReason,
        string nameEn,
        string nameAr,
        bool isActive,
        string? updatedBy,
        DateTime utcNow)
    {
        Apply(faStatus, cancelReason, closureReason, nameEn, nameAr, isActive);
        Touch(updatedBy, utcNow);
    }

    public void SetActive(bool isActive, string? updatedBy, DateTime utcNow)
    {
        IsActive = isActive;
        Touch(updatedBy, utcNow);
    }

    private void Apply(string faStatus, string? cancelReason, string? closureReason, string nameEn, string nameAr, bool isActive)
    {
        var status = faStatus?.Trim().ToUpperInvariant();
        if (!C2mOperationStatuses.IsDefined(status))
        {
            throw new DomainException($"FA status must be '{C2mOperationStatuses.Completed}' or '{C2mOperationStatuses.Cancelled}'.");
        }

        if (string.IsNullOrWhiteSpace(nameEn) || string.IsNullOrWhiteSpace(nameAr))
        {
            throw new DomainException("An action mapping must have an English and an Arabic name.");
        }

        // C2M rejects the reason that does not belong to the status, so only the matching one is kept.
        FaStatus = status!;
        CancelReason = status == C2mOperationStatuses.Cancelled ? Normalize(cancelReason) : null;
        ClosureReason = status == C2mOperationStatuses.Completed ? Normalize(closureReason) : null;
        NameEn = nameEn.Trim();
        NameAr = nameAr.Trim();
        IsActive = isActive;
    }

    private void Touch(string? actor, DateTime utcNow)
    {
        UpdatedBy = Normalize(actor) ?? UpdatedBy;
        SetUpdated(utcNow);
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
