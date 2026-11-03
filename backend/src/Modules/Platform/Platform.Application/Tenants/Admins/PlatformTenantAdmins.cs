using Platform.Application.Common.Interfaces;
using Platform.Application.Common.Security;
using Tenancy.Application.Common.Interfaces;
using Tenancy.Domain.Constants;

namespace Platform.Application.Tenants.Admins;

/// <summary>What the console tells staff after credentials were (or were not) e-mailed.</summary>
public sealed record PlatformTenantAdminCredentialsDto(string UserId, string Delivery);

/// <summary>The tenant both from the snapshot (to open its database) and the catalog (its name and language).</summary>
internal sealed record PlatformTenantAdminTarget(TenantInfo Info, string Name, string Language);

internal static class PlatformTenantAdminAccess
{
    public const string TenantNotFound = "Workspace not found.";
    public const string NotReady = "The workspace is not ready: its administrators can be managed once it is provisioned.";
    public const string EmailDisabled = "E-mail is switched off, so no credentials were sent and the account was not changed.";

    public static async Task<Result<PlatformTenantAdminTarget>> FindAsync(ICatalogDbContext catalog, ITenantStore tenantStore,
        Guid tenantId, CancellationToken cancellationToken)
    {
        var tenant = await catalog.Tenants.AsNoTracking()
            .Where(t => t.Id == tenantId)
            .Select(t => new { t.Name, t.Status, t.Settings.DefaultLanguage, HasDatabase = t.Database != null })
            .FirstOrDefaultAsync(cancellationToken);

        var info = tenantStore.FindById(tenantId);

        if (tenant is null || info is null)
        {
            return PlatformResults.Fail<PlatformTenantAdminTarget>(TenantNotFound, ApiErrorCodes.TenantNotFound, httpStatusCode: 404);
        }

        if (!tenant.HasDatabase || tenant.Status is TenantStatus.Provisioning or TenantStatus.Deleted)
        {
            return PlatformResults.Fail<PlatformTenantAdminTarget>(NotReady, ApiErrorCodes.BackendBusinessRuleValidation, httpStatusCode: 409);
        }

        return Result<PlatformTenantAdminTarget>.Success(new PlatformTenantAdminTarget(info, tenant.Name, tenant.DefaultLanguage));
    }

    public static async Task<Result<PlatformTenantAdminCredentialsDto>> SendCredentialsAsync(
        ITenantAdministratorDirectory directory,
        IPlatformAuditWriter audit,
        ICatalogDbContext catalog,
        PlatformTenantAdminTarget target,
        string userId,
        CancellationToken cancellationToken)
    {
        var sent = await directory.SendCredentialsAsync(
            new TenantCredentialsRequest(target.Info, userId, target.Language, target.Name, TenantCredentialsEmail.Administrator),
            cancellationToken);

        if (!sent.IsSuccess)
        {
            return Result.Failure<PlatformTenantAdminCredentialsDto>(sent.Error);
        }

        var delivered = sent.Value != TenantCredentialsDelivery.EmailDisabled;
        audit.Add(delivered ? PlatformAuditActions.TenantAdminCredentialsSent : PlatformAuditActions.CredentialsNotSent,
            target.Info.Id, nameof(Tenant), userId, delivered ? sent.Value.ToString() : EmailDisabled);
        await catalog.SaveChangesAsync(cancellationToken);

        return Result<PlatformTenantAdminCredentialsDto>.Success(new PlatformTenantAdminCredentialsDto(userId, sent.Value.ToString()));
    }
}

[PlatformAuthorize]
public sealed record GetPlatformTenantAdminsQuery(Guid TenantId) : IRequest<Result<IReadOnlyList<TenantAdministratorDto>>>;

public sealed class GetPlatformTenantAdminsQueryValidator : AbstractValidator<GetPlatformTenantAdminsQuery>
{
    public GetPlatformTenantAdminsQueryValidator()
    {
        RuleFor(x => x.TenantId).NotEmpty().WithMessage("Workspace id is required.");
    }
}

public sealed class GetPlatformTenantAdminsQueryHandler(
    ICatalogDbContext catalog,
    ITenantStore tenantStore,
    ITenantAdministratorDirectory directory)
    : IRequestHandler<GetPlatformTenantAdminsQuery, Result<IReadOnlyList<TenantAdministratorDto>>>
{
    public async Task<Result<IReadOnlyList<TenantAdministratorDto>>> Handle(GetPlatformTenantAdminsQuery request, CancellationToken cancellationToken)
    {
        var target = await PlatformTenantAdminAccess.FindAsync(catalog, tenantStore, request.TenantId, cancellationToken);

        return target.IsSuccess
            ? Result<IReadOnlyList<TenantAdministratorDto>>.Success(await directory.ListAsync(target.Value!.Info, cancellationToken))
            : Result.Failure<IReadOnlyList<TenantAdministratorDto>>(target.Error);
    }
}

/// <summary>
/// Adds another Administrator to the workspace and e-mails a temporary password and a set-password
/// link. The plan's user limit applies, as it does in the tenant app.
/// </summary>
[PlatformAuthorize(Roles = PlatformRoles.Admin)]
public sealed record CreatePlatformTenantAdminCommand : IRequest<Result<PlatformTenantAdminCredentialsDto>>
{
    public const int EmailMaxLength = 256;
    public const int PhoneMaxLength = 30;

    public Guid TenantId { get; init; }

    public string Email { get; init; } = default!;

    public string? PhoneNumber { get; init; }
}

public sealed class CreatePlatformTenantAdminCommandValidator : AbstractValidator<CreatePlatformTenantAdminCommand>
{
    public CreatePlatformTenantAdminCommandValidator()
    {
        RuleFor(x => x.TenantId).NotEmpty().WithMessage("Workspace id is required.");
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("E-mail is required.")
            .EmailAddress().WithMessage("E-mail is not valid.")
            .MaximumLength(CreatePlatformTenantAdminCommand.EmailMaxLength)
            .WithMessage($"E-mail must not exceed {CreatePlatformTenantAdminCommand.EmailMaxLength} characters.");
        RuleFor(x => x.PhoneNumber)
            .MaximumLength(CreatePlatformTenantAdminCommand.PhoneMaxLength)
            .WithMessage($"Phone number must not exceed {CreatePlatformTenantAdminCommand.PhoneMaxLength} characters.");
    }
}

public sealed class CreatePlatformTenantAdminCommandHandler(
    ICatalogDbContext catalog,
    ITenantStore tenantStore,
    ITenantAdministratorDirectory directory,
    IPlatformAuditWriter audit)
    : IRequestHandler<CreatePlatformTenantAdminCommand, Result<PlatformTenantAdminCredentialsDto>>
{
    public async Task<Result<PlatformTenantAdminCredentialsDto>> Handle(CreatePlatformTenantAdminCommand request, CancellationToken cancellationToken)
    {
        var target = await PlatformTenantAdminAccess.FindAsync(catalog, tenantStore, request.TenantId, cancellationToken);
        if (!target.IsSuccess)
        {
            return Result.Failure<PlatformTenantAdminCredentialsDto>(target.Error);
        }

        var phone = string.IsNullOrWhiteSpace(request.PhoneNumber) ? null : request.PhoneNumber.Trim();
        var created = await directory.CreateAsync(target.Value!.Info, request.Email, phone, cancellationToken);
        if (!created.IsSuccess)
        {
            return Result.Failure<PlatformTenantAdminCredentialsDto>(created.Error);
        }

        var userId = created.Value!;
        audit.Add(PlatformAuditActions.TenantAdminCreated, target.Value.Info.Id, nameof(Tenant), userId, request.Email.Trim());
        await catalog.SaveChangesAsync(cancellationToken);

        return await PlatformTenantAdminAccess.SendCredentialsAsync(directory, audit, catalog, target.Value, userId, cancellationToken);
    }
}

/// <summary>Suspends or reactivates one administrator. The workspace's last active administrator cannot be suspended.</summary>
[PlatformAuthorize(Roles = PlatformRoles.Admin)]
public sealed record ChangePlatformTenantAdminStatusCommand : IRequest<Result<TenantAdministratorDto>>
{
    public const int ReasonMaxLength = 500;

    public Guid TenantId { get; init; }

    /// <summary>Taken from the route; nullable so body binding does not demand it.</summary>
    public string? UserId { get; init; }

    public bool IsEnabled { get; init; }

    public string? Reason { get; init; }
}

public sealed class ChangePlatformTenantAdminStatusCommandValidator : AbstractValidator<ChangePlatformTenantAdminStatusCommand>
{
    public ChangePlatformTenantAdminStatusCommandValidator()
    {
        RuleFor(x => x.TenantId).NotEmpty().WithMessage("Workspace id is required.");
        RuleFor(x => x.UserId).NotEmpty().WithMessage("Administrator id is required.");
        RuleFor(x => x.Reason)
            .NotEmpty().When(x => !x.IsEnabled).WithMessage("A reason is required to suspend an administrator.")
            .MaximumLength(ChangePlatformTenantAdminStatusCommand.ReasonMaxLength)
            .WithMessage($"Reason must not exceed {ChangePlatformTenantAdminStatusCommand.ReasonMaxLength} characters.");
    }
}

public sealed class ChangePlatformTenantAdminStatusCommandHandler(
    ICatalogDbContext catalog,
    ITenantStore tenantStore,
    ITenantAdministratorDirectory directory,
    IPlatformAuditWriter audit)
    : IRequestHandler<ChangePlatformTenantAdminStatusCommand, Result<TenantAdministratorDto>>
{
    public async Task<Result<TenantAdministratorDto>> Handle(ChangePlatformTenantAdminStatusCommand request, CancellationToken cancellationToken)
    {
        var target = await PlatformTenantAdminAccess.FindAsync(catalog, tenantStore, request.TenantId, cancellationToken);
        if (!target.IsSuccess)
        {
            return Result.Failure<TenantAdministratorDto>(target.Error);
        }

        var userId = request.UserId!;
        var changed = await directory.SetEnabledAsync(target.Value!.Info, userId, request.IsEnabled, cancellationToken);
        if (!changed.IsSuccess)
        {
            return changed;
        }

        var details = $"{changed.Value!.UserName}{(string.IsNullOrWhiteSpace(request.Reason) ? string.Empty : $": {request.Reason.Trim()}")}";
        audit.Add(request.IsEnabled ? PlatformAuditActions.TenantAdminReactivated : PlatformAuditActions.TenantAdminSuspended,
            target.Value.Info.Id, nameof(Tenant), userId, details);
        await catalog.SaveChangesAsync(cancellationToken);

        return changed;
    }
}

/// <summary>
/// Issues a new temporary password and set-password link and e-mails them — for an administrator
/// who never received or lost the first ones. Any earlier link stops working.
/// </summary>
[PlatformAuthorize(Roles = PlatformRoles.Admin)]
public sealed record ResendPlatformTenantAdminCredentialsCommand(Guid TenantId, string UserId)
    : IRequest<Result<PlatformTenantAdminCredentialsDto>>;

public sealed class ResendPlatformTenantAdminCredentialsCommandValidator : AbstractValidator<ResendPlatformTenantAdminCredentialsCommand>
{
    public ResendPlatformTenantAdminCredentialsCommandValidator()
    {
        RuleFor(x => x.TenantId).NotEmpty().WithMessage("Workspace id is required.");
        RuleFor(x => x.UserId).NotEmpty().WithMessage("Administrator id is required.");
    }
}

public sealed class ResendPlatformTenantAdminCredentialsCommandHandler(
    ICatalogDbContext catalog,
    ITenantStore tenantStore,
    ITenantAdministratorDirectory directory,
    IPlatformAuditWriter audit)
    : IRequestHandler<ResendPlatformTenantAdminCredentialsCommand, Result<PlatformTenantAdminCredentialsDto>>
{
    public async Task<Result<PlatformTenantAdminCredentialsDto>> Handle(ResendPlatformTenantAdminCredentialsCommand request, CancellationToken cancellationToken)
    {
        var target = await PlatformTenantAdminAccess.FindAsync(catalog, tenantStore, request.TenantId, cancellationToken);

        return target.IsSuccess
            ? await PlatformTenantAdminAccess.SendCredentialsAsync(directory, audit, catalog, target.Value!, request.UserId, cancellationToken)
            : Result.Failure<PlatformTenantAdminCredentialsDto>(target.Error);
    }
}
