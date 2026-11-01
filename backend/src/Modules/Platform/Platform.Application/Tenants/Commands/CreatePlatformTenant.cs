using Microsoft.Extensions.Options;
using NWFM.Shared.Options;
using Platform.Application.Common.Interfaces;
using Platform.Application.Common.Security;
using Platform.Application.Signup.Common;
using Tenancy.Application.Common.Interfaces;
using Tenancy.Domain.Constants;

namespace Platform.Application.Tenants.Commands;

public sealed record CreatePlatformTenantDto(Guid SignupId, string TenantKey);

/// <summary>
/// Staff create a workspace by hand. It runs the customer signup pipeline — same request row, same
/// provisioner, same Standard seed and activation e-mail — with the e-mail confirmation already
/// satisfied, since staff typed the address themselves. Managed database only.
/// </summary>
[PlatformAuthorize(Roles = PlatformRoles.Admin)]
public sealed record CreatePlatformTenantCommand : IRequest<Result<CreatePlatformTenantDto>>
{
    public string TenantKey { get; init; } = default!;

    public string OrganizationName { get; init; } = default!;

    public string AdminFullName { get; init; } = default!;

    public string AdminEmail { get; init; } = default!;

    public string? AdminPhone { get; init; }

    /// <summary>Empty picks the default trial plan. Unlike public signup, a non-public plan is allowed.</summary>
    public string? PlanCode { get; init; }

    /// <summary><c>en</c> or <c>ar</c>: the workspace's default language and the e-mails' language.</summary>
    public string? Language { get; init; }
}

public sealed class CreatePlatformTenantCommandValidator : AbstractValidator<CreatePlatformTenantCommand>
{
    public CreatePlatformTenantCommandValidator()
    {
        RuleFor(x => x.TenantKey)
            .NotEmpty().WithMessage("Workspace address is required.")
            .Must(Tenancy.Domain.Entities.TenantKey.IsValid)
            .WithMessage($"Workspace address must be {Tenancy.Domain.Entities.TenantKey.MinLength}-{Tenancy.Domain.Entities.TenantKey.MaxLength} lowercase letters, digits or single hyphens, starting with a letter.");

        RuleFor(x => x.OrganizationName)
            .NotEmpty().WithMessage("Organisation name is required.")
            .MaximumLength(SignupRequest.NameMaxLength).WithMessage($"Organisation name must not exceed {SignupRequest.NameMaxLength} characters.");

        RuleFor(x => x.AdminFullName)
            .NotEmpty().WithMessage("Administrator name is required.")
            .MaximumLength(SignupRequest.NameMaxLength).WithMessage($"Administrator name must not exceed {SignupRequest.NameMaxLength} characters.");

        RuleFor(x => x.AdminEmail)
            .NotEmpty().WithMessage("Administrator e-mail is required.")
            .EmailAddress().WithMessage("Administrator e-mail is not valid.")
            .MaximumLength(SignupRequest.EmailMaxLength).WithMessage($"Administrator e-mail must not exceed {SignupRequest.EmailMaxLength} characters.");

        RuleFor(x => x.AdminPhone)
            .MaximumLength(SignupRequest.PhoneMaxLength).WithMessage($"Phone must not exceed {SignupRequest.PhoneMaxLength} characters.");

        RuleFor(x => x.Language)
            .Must(language => language is null or TenantSettings.Languages.English or TenantSettings.Languages.Arabic)
            .WithMessage("Language must be 'en' or 'ar'.");
    }
}

public sealed class CreatePlatformTenantCommandHandler(
    ICatalogDbContext catalog,
    ISignupProvisioningQueue provisioningQueue,
    IClientIpAccessor clientIp,
    IPlatformAuditWriter audit,
    IOptions<PlatformOptions> platformOptions,
    IOptions<TenancyOptions> tenancyOptions,
    TimeProvider timeProvider)
    : IRequestHandler<CreatePlatformTenantCommand, Result<CreatePlatformTenantDto>>
{
    private const string KeyUnavailableMessage = "This workspace address is not available.";
    private const string NoDatabaseServerMessage = "No active database server has room for a new workspace. Register one under Database servers first.";

    public async Task<Result<CreatePlatformTenantDto>> Handle(CreatePlatformTenantCommand request, CancellationToken cancellationToken)
    {
        var options = platformOptions.Value;
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var key = TenantKey.Normalize(request.TenantKey);

        var planCode = string.IsNullOrWhiteSpace(request.PlanCode) ? options.DefaultPlanCode : request.PlanCode.Trim().ToLowerInvariant();
        if (!await catalog.Plans.AnyAsync(p => p.Code == planCode && p.IsActive, cancellationToken))
        {
            return PlatformResults.Fail<CreatePlatformTenantDto>("The selected plan is not available.", ApiErrorCodes.ValidationError, httpStatusCode: 400);
        }

        if (await SignupKeyAvailability.UnavailableReasonAsync(catalog, tenancyOptions.Value, key, now, cancellationToken) is not null)
        {
            return PlatformResults.Fail<CreatePlatformTenantDto>(KeyUnavailableMessage, ApiErrorCodes.TenantKeyUnavailable, httpStatusCode: 409);
        }

        // Checked up front so staff hear it now instead of finding a failed job on the signups page.
        var hasServer = await catalog.DatabaseServers.AnyAsync(
            s => s.IsActive && catalog.TenantDatabases.Count(d => d.DatabaseServerId == s.Id) < s.MaxDatabases,
            cancellationToken);
        if (!hasServer)
        {
            return PlatformResults.Fail<CreatePlatformTenantDto>(NoDatabaseServerMessage, ApiErrorCodes.BackendBusinessRuleValidation, httpStatusCode: 422);
        }

        // The token is never sent anywhere: it only exists so the signup is confirmed through the
        // same domain rule a customer's link goes through.
        var tokenHash = SignupTokens.Hash(SignupTokens.Generate());
        var signup = SignupRequest.Create(
            key,
            request.OrganizationName,
            request.AdminFullName,
            request.AdminEmail.Trim(),
            request.AdminPhone,
            planCode,
            request.Language,
            TenantDatabaseMode.Managed,
            tokenHash,
            now.AddHours(options.SignupEmailTokenHours),
            clientIp.ClientIp,
            approvalRequired: false,
            now);

        signup.VerifyEmail(tokenHash, now);
        signup.Queue(now);

        catalog.SignupRequests.Add(signup);
        audit.Add(PlatformAuditActions.SignupCreatedByStaff, null, nameof(SignupRequest), signup.Id.ToString(), $"{key} on plan {planCode}");
        await catalog.SaveChangesAsync(cancellationToken);

        provisioningQueue.Enqueue(signup.Id);

        return Result<CreatePlatformTenantDto>.Success(new CreatePlatformTenantDto(signup.Id, signup.TenantKey));
    }
}
