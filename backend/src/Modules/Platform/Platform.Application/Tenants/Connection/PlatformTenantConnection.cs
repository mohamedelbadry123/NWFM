using Platform.Application.Common.Interfaces;
using Platform.Application.Common.Security;
using Tenancy.Application.Common.Interfaces;
using Tenancy.Domain.Constants;

namespace Platform.Application.Tenants.Connection;

internal static class PlatformTenantConnectionMessages
{
    public const string TenantNotFound = "Workspace not found.";
    public const string Unreadable = "This workspace has no database registered, or its connection string cannot be read.";
}

/// <summary>Where the tenant's database is, with the password masked. Admin only.</summary>
[PlatformAuthorize(Roles = PlatformRoles.Admin)]
public sealed record GetPlatformTenantConnectionQuery(Guid TenantId) : IRequest<Result<TenantConnectionDto>>;

public sealed class GetPlatformTenantConnectionQueryValidator : AbstractValidator<GetPlatformTenantConnectionQuery>
{
    public GetPlatformTenantConnectionQueryValidator()
    {
        RuleFor(x => x.TenantId).NotEmpty().WithMessage("Workspace id is required.");
    }
}

public sealed class GetPlatformTenantConnectionQueryHandler(ITenantStore tenantStore, ITenantConnectionViewer viewer)
    : IRequestHandler<GetPlatformTenantConnectionQuery, Result<TenantConnectionDto>>
{
    public Task<Result<TenantConnectionDto>> Handle(GetPlatformTenantConnectionQuery request, CancellationToken cancellationToken)
    {
        var tenant = tenantStore.FindById(request.TenantId);
        if (tenant is null)
        {
            return Task.FromResult(PlatformResults.Fail<TenantConnectionDto>(
                PlatformTenantConnectionMessages.TenantNotFound, ApiErrorCodes.TenantNotFound, httpStatusCode: 404));
        }

        var connection = viewer.Describe(tenant, reveal: false);
        return Task.FromResult(connection is null
            ? PlatformResults.Fail<TenantConnectionDto>(PlatformTenantConnectionMessages.Unreadable, ApiErrorCodes.NotFound, httpStatusCode: 404)
            : Result<TenantConnectionDto>.Success(connection));
    }
}

/// <summary>
/// The full connection string, password included, for hands-on support. Admin only; a reason is
/// required and every reveal is written to the platform audit log.
/// </summary>
[PlatformAuthorize(Roles = PlatformRoles.Admin)]
public sealed record RevealPlatformTenantConnectionCommand : IRequest<Result<TenantConnectionDto>>
{
    public const int ReasonMaxLength = 500;

    public Guid TenantId { get; init; }

    public string Reason { get; init; } = default!;
}

public sealed class RevealPlatformTenantConnectionCommandValidator : AbstractValidator<RevealPlatformTenantConnectionCommand>
{
    public RevealPlatformTenantConnectionCommandValidator()
    {
        RuleFor(x => x.TenantId).NotEmpty().WithMessage("Workspace id is required.");
        RuleFor(x => x.Reason)
            .NotEmpty().WithMessage("A reason is required to reveal the connection string.")
            .MaximumLength(RevealPlatformTenantConnectionCommand.ReasonMaxLength)
            .WithMessage($"Reason must not exceed {RevealPlatformTenantConnectionCommand.ReasonMaxLength} characters.");
    }
}

public sealed class RevealPlatformTenantConnectionCommandHandler(
    ICatalogDbContext catalog,
    ITenantStore tenantStore,
    ITenantConnectionViewer viewer,
    IPlatformAuditWriter audit)
    : IRequestHandler<RevealPlatformTenantConnectionCommand, Result<TenantConnectionDto>>
{
    public async Task<Result<TenantConnectionDto>> Handle(RevealPlatformTenantConnectionCommand request, CancellationToken cancellationToken)
    {
        var tenant = tenantStore.FindById(request.TenantId);
        if (tenant is null)
        {
            return PlatformResults.Fail<TenantConnectionDto>(PlatformTenantConnectionMessages.TenantNotFound, ApiErrorCodes.TenantNotFound, httpStatusCode: 404);
        }

        var connection = viewer.Describe(tenant, reveal: true);
        if (connection is null)
        {
            return PlatformResults.Fail<TenantConnectionDto>(PlatformTenantConnectionMessages.Unreadable, ApiErrorCodes.NotFound, httpStatusCode: 404);
        }

        // Saved before the secret leaves: no reveal goes unrecorded.
        audit.Add(PlatformAuditActions.ConnectionStringRevealed, tenant.Id, nameof(Tenant), tenant.Key, request.Reason.Trim());
        await catalog.SaveChangesAsync(cancellationToken);

        return Result<TenantConnectionDto>.Success(connection);
    }
}
