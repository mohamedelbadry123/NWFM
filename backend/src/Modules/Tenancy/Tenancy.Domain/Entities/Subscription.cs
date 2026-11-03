namespace Tenancy.Domain.Entities;

/// <summary>
/// A tenant's current plan and billing state. Billing is manual: platform staff activate, extend or
/// suspend; nothing here talks to a payment provider.
/// </summary>
public sealed class Subscription
{
    public const int NotesMaxLength = 1000;

    private Subscription()
    {
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public int PlanId { get; private set; }

    public Plan Plan { get; private set; } = default!;

    public SubscriptionStatus Status { get; private set; }

    public DateTime StartedAtUtc { get; private set; }

    public DateTime? TrialEndsAtUtc { get; private set; }

    /// <summary>Paid period end; null on an active subscription means open-ended.</summary>
    public DateTime? CurrentPeriodEndsAtUtc { get; private set; }

    public string? Notes { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    public static Subscription Start(Guid tenantId, Plan plan, DateTime utcNow)
    {
        if (!plan.IsActive)
        {
            throw new DomainException("The selected plan is not available.");
        }

        var isTrial = plan.TrialDays > 0;

        return new Subscription
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            PlanId = plan.Id,
            Plan = plan,
            Status = isTrial ? SubscriptionStatus.Trial : SubscriptionStatus.Active,
            StartedAtUtc = utcNow,
            TrialEndsAtUtc = isTrial ? utcNow.AddDays(plan.TrialDays) : null,
            UpdatedAtUtc = utcNow
        };
    }

    /// <summary>Whether the tenant may be served. A lapsed trial or period stops service without a status write.</summary>
    public bool IsUsable(DateTime utcNow) => Status switch
    {
        SubscriptionStatus.Trial => TrialEndsAtUtc is null || TrialEndsAtUtc > utcNow,
        SubscriptionStatus.Active => CurrentPeriodEndsAtUtc is null || CurrentPeriodEndsAtUtc > utcNow,
        _ => false
    };

    public void Activate(DateTime? periodEndsAtUtc, string? notes, DateTime utcNow)
    {
        if (periodEndsAtUtc is not null && periodEndsAtUtc <= utcNow)
        {
            throw new DomainException("The paid period must end in the future.");
        }

        Status = SubscriptionStatus.Active;
        CurrentPeriodEndsAtUtc = periodEndsAtUtc;
        SetNotes(notes);
        UpdatedAtUtc = utcNow;
    }

    public void ExtendTrial(int days, DateTime utcNow)
    {
        if (days < 1)
        {
            throw new DomainException("A trial is extended by at least one day.");
        }

        var from = TrialEndsAtUtc is { } end && end > utcNow ? end : utcNow;
        Status = SubscriptionStatus.Trial;
        TrialEndsAtUtc = from.AddDays(days);
        UpdatedAtUtc = utcNow;
    }

    public void ChangePlan(Plan plan, DateTime utcNow)
    {
        if (!plan.IsActive)
        {
            throw new DomainException("The selected plan is not available.");
        }

        PlanId = plan.Id;
        Plan = plan;
        UpdatedAtUtc = utcNow;
    }

    public void Suspend(string? notes, DateTime utcNow)
    {
        Status = SubscriptionStatus.Suspended;
        SetNotes(notes);
        UpdatedAtUtc = utcNow;
    }

    public void Cancel(string? notes, DateTime utcNow)
    {
        Status = SubscriptionStatus.Cancelled;
        SetNotes(notes);
        UpdatedAtUtc = utcNow;
    }

    private void SetNotes(string? notes)
    {
        if (notes is null)
        {
            return;
        }

        Notes = notes.Length > NotesMaxLength ? notes[..NotesMaxLength] : notes;
    }
}
