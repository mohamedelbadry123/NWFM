using Platform.Application.Common.Security;
using Platform.Application.Tenants.Common;
using Platform.Application.Tenants.Models;
using Tenancy.Application.Common.Interfaces;
using Tenancy.Domain.Constants;

namespace Platform.Application.Tenants.Commands;

public enum PlatformTenantStatusAction
{
    Activate = 1,
    Suspend = 2,
    Maintenance = 3
}

/// <summary>
/// Activates, suspends or puts a tenant into maintenance. Suspension and maintenance refuse every
/// request to the tenant within one snapshot refresh; data is untouched.
/// </summary>
[PlatformAuthorize(Roles = PlatformRoles.Admin)]
public sealed record ChangePlatformTenantStatusCommand : IRequest<Result<PlatformTenantDetailDto>>
{
    public Guid TenantId { get; init; }

    public PlatformTenantStatusAction Action { get; init; }

    public string? Reason { get; init; }
}

public sealed class ChangePlatformTenantStatusCommandValidator : AbstractValidator<ChangePlatformTenantStatusCommand>
{
    public ChangePlatformTenantStatusCommandValidator()
    {
        RuleFor(x => x.TenantId).NotEmpty().WithMessage("Workspace id is required.");
        RuleFor(x => x.Action).IsInEnum().WithMessage("Status action is not recognized.");

        RuleFor(x => x.Reason)
            .NotEmpty().WithMessage("A reason is required to suspend a workspace or put it into maintenance.")
            .When(x => x.Action != PlatformTenantStatusAction.Activate);

        RuleFor(x => x.Reason)
            .MaximumLength(Tenant.StatusReasonMaxLength).WithMessage($"Reason must not exceed {Tenant.StatusReasonMaxLength} characters.");
    }
}

public sealed class ChangePlatformTenantStatusCommandHandler(
    ICatalogDbContext catalog,
    ITenantStore tenantStore,
    IPlatformAuditWriter audit,
    TimeProvider timeProvider)
    : IRequestHandler<ChangePlatformTenantStatusCommand, Result<PlatformTenantDetailDto>>
{
    public async Task<Result<PlatformTenantDetailDto>> Handle(ChangePlatformTenantStatusCommand request, CancellationToken cancellationToken)
    {
        var tenant = await PlatformTenantQueries.LoadAsync(catalog, request.TenantId, cancellationToken);

        if (tenant is null)
        {
            return PlatformResults.Fail<PlatformTenantDetailDto>("Workspace not found.", ApiErrorCodes.TenantNotFound, httpStatusCode: 404);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        string action;

        switch (request.Action)
        {
            case PlatformTenantStatusAction.Activate:
                tenant.Activate(now);
                action = PlatformAuditActions.TenantActivated;
                break;
            case PlatformTenantStatusAction.Suspend:
                tenant.Suspend(request.Reason!, now);
                action = PlatformAuditActions.TenantSuspended;
                break;
            default:
                tenant.EnterMaintenance(request.Reason!, now);
                action = PlatformAuditActions.TenantMaintenance;
                break;
        }

        audit.Add(action, tenant.Id, nameof(Tenant), tenant.Key, request.Reason);
        await catalog.SaveChangesAsync(cancellationToken);

        PlatformTenantQueries.Publish(tenantStore);
        return Result<PlatformTenantDetailDto>.Success(tenant.ToDetailDto());
    }
}
