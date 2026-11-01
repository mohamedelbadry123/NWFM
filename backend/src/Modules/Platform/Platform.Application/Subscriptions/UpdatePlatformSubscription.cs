using Platform.Application.Common.Security;
using Platform.Application.Tenants.Common;
using Platform.Application.Tenants.Models;
using Tenancy.Application.Common.Interfaces;
using Tenancy.Domain.Constants;

namespace Platform.Application.Subscriptions;

public enum PlatformSubscriptionAction
{
    /// <summary>Paid / approved: service until <c>PeriodEndsAtUtc</c>, or open-ended when null.</summary>
    Activate = 1,
    ExtendTrial = 2,
    ChangePlan = 3,
    Suspend = 4,
    Cancel = 5
}

/// <summary>
/// Manual subscription management — there is no payment provider. A lapsed trial or period stops
/// service on its own; these actions are how staff start, extend, change or end it.
/// </summary>
[PlatformAuthorize(Roles = PlatformRoles.Admin)]
public sealed record UpdatePlatformSubscriptionCommand : IRequest<Result<PlatformTenantDetailDto>>
{
    public Guid TenantId { get; init; }

    public PlatformSubscriptionAction Action { get; init; }

    public DateTime? PeriodEndsAtUtc { get; init; }

    public int? TrialDays { get; init; }

    public string? PlanCode { get; init; }

    public string? Notes { get; init; }
}

public sealed class UpdatePlatformSubscriptionCommandValidator : AbstractValidator<UpdatePlatformSubscriptionCommand>
{
    public UpdatePlatformSubscriptionCommandValidator()
    {
        RuleFor(x => x.TenantId).NotEmpty().WithMessage("Workspace id is required.");
        RuleFor(x => x.Action).IsInEnum().WithMessage("Subscription action is not recognized.");

        RuleFor(x => x.TrialDays)
            .NotNull().WithMessage("Number of trial days is required.")
            .InclusiveBetween(1, 365).WithMessage("A trial is extended by 1 to 365 days.")
            .When(x => x.Action == PlatformSubscriptionAction.ExtendTrial);

        RuleFor(x => x.PlanCode)
            .NotEmpty().WithMessage("Plan is required.")
            .When(x => x.Action == PlatformSubscriptionAction.ChangePlan);

        RuleFor(x => x.Notes)
            .MaximumLength(Subscription.NotesMaxLength).WithMessage($"Notes must not exceed {Subscription.NotesMaxLength} characters.");
    }
}

public sealed class UpdatePlatformSubscriptionCommandHandler(
    ICatalogDbContext catalog,
    ITenantStore tenantStore,
    IPlatformAuditWriter audit,
    TimeProvider timeProvider)
    : IRequestHandler<UpdatePlatformSubscriptionCommand, Result<PlatformTenantDetailDto>>
{
    public async Task<Result<PlatformTenantDetailDto>> Handle(UpdatePlatformSubscriptionCommand request, CancellationToken cancellationToken)
    {
        var tenant = await PlatformTenantQueries.LoadAsync(catalog, request.TenantId, cancellationToken);

        if (tenant is null)
        {
            return PlatformResults.Fail<PlatformTenantDetailDto>("Workspace not found.", ApiErrorCodes.TenantNotFound, httpStatusCode: 404);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var plan = request.PlanCode is null
            ? null
            : await catalog.Plans.FirstOrDefaultAsync(p => p.Code == request.PlanCode, cancellationToken);

        if (request.PlanCode is not null && plan is null)
        {
            return PlatformResults.Fail<PlatformTenantDetailDto>("Plan not found.", ApiErrorCodes.NotFound, httpStatusCode: 404);
        }

        if (tenant.Subscription is not { } subscription)
        {
            // A tenant registered without one (an interrupted signup) gets it here.
            plan ??= await catalog.Plans.FirstAsync(p => p.IsActive, cancellationToken);
            subscription = Subscription.Start(tenant.Id, plan, now);
            tenant.StartSubscription(subscription);
        }

        switch (request.Action)
        {
            case PlatformSubscriptionAction.Activate:
                if (plan is not null)
                {
                    subscription.ChangePlan(plan, now);
                }

                subscription.Activate(request.PeriodEndsAtUtc, request.Notes, now);
                break;
            case PlatformSubscriptionAction.ExtendTrial:
                subscription.ExtendTrial(request.TrialDays!.Value, now);
                break;
            case PlatformSubscriptionAction.ChangePlan:
                subscription.ChangePlan(plan!, now);
                break;
            case PlatformSubscriptionAction.Suspend:
                subscription.Suspend(request.Notes, now);
                break;
            default:
                subscription.Cancel(request.Notes, now);
                break;
        }

        audit.Add(PlatformAuditActions.SubscriptionChanged, tenant.Id, nameof(Subscription), subscription.Id.ToString(),
            $"{request.Action}: {subscription.Status} on {subscription.Plan.Code}");
        await catalog.SaveChangesAsync(cancellationToken);

        PlatformTenantQueries.Publish(tenantStore);
        return Result<PlatformTenantDetailDto>.Success(tenant.ToDetailDto());
    }
}
