using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NWFM.Shared.Messaging;
using NWFM.Shared.Options;
using Platform.Application.Common.Interfaces;
using Platform.Application.Signup.Common;
using Tenancy.Application.Common.Interfaces;

namespace Platform.Application.Signup.Public;

public enum SignupDatabaseMode
{
    /// <summary>We create and host the database.</summary>
    Managed = 1,

    /// <summary>The customer's own SQL Server; connection details come after the e-mail is confirmed.</summary>
    BringYourOwn = 2
}

public sealed record SubmitSignupDto(Guid SignupId, string AdminEmail);

/// <summary>
/// Starts a signup. Nothing is created until the e-mail address is confirmed through the link this
/// sends — the request only holds the workspace address meanwhile.
/// </summary>
public sealed record SubmitSignupCommand : IRequest<Result<SubmitSignupDto>>
{
    public string TenantKey { get; init; } = default!;

    public string OrganizationName { get; init; } = default!;

    public string AdminFullName { get; init; } = default!;

    public string AdminEmail { get; init; } = default!;

    public string? AdminPhone { get; init; }

    /// <summary>Empty picks the default trial plan.</summary>
    public string? PlanCode { get; init; }

    /// <summary><c>en</c> or <c>ar</c>: the workspace's default language and the e-mails' language.</summary>
    public string? Language { get; init; }

    public SignupDatabaseMode DatabaseMode { get; init; } = SignupDatabaseMode.Managed;

    public string? CaptchaToken { get; init; }
}

public sealed class SubmitSignupCommandValidator : AbstractValidator<SubmitSignupCommand>
{
    public SubmitSignupCommandValidator()
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

        RuleFor(x => x.DatabaseMode).IsInEnum().WithMessage("Database option is not recognized.");
    }
}

public sealed class SubmitSignupCommandHandler(
    ICatalogDbContext catalog,
    ICaptchaVerifier captcha,
    IClientIpAccessor clientIp,
    IEmailService emailService,
    IPlatformAuditWriter audit,
    IOptions<PlatformOptions> platformOptions,
    IOptions<TenancyOptions> tenancyOptions,
    TimeProvider timeProvider,
    ILogger<SubmitSignupCommandHandler> logger)
    : IRequestHandler<SubmitSignupCommand, Result<SubmitSignupDto>>
{
    private const int MaxSignupsPerEmailPerDay = 5;
    private const string KeyUnavailableMessage = "This workspace address is not available.";

    public async Task<Result<SubmitSignupDto>> Handle(SubmitSignupCommand request, CancellationToken cancellationToken)
    {
        var ip = clientIp.ClientIp;

        if (!await captcha.VerifyAsync(request.CaptchaToken, ip, cancellationToken))
        {
            return PlatformResults.Fail<SubmitSignupDto>("The security check failed. Please try again.", ApiErrorCodes.CaptchaFailed, httpStatusCode: 400);
        }

        var options = platformOptions.Value;
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var key = TenantKey.Normalize(request.TenantKey);
        var email = request.AdminEmail.Trim();

        var since = now.AddDays(-1);
        var recentForEmail = await catalog.SignupRequests.CountAsync(s => s.AdminEmail == email && s.CreatedAtUtc > since, cancellationToken);
        if (recentForEmail >= MaxSignupsPerEmailPerDay)
        {
            return PlatformResults.Fail<SubmitSignupDto>("Too many signups for this e-mail address today.", ApiErrorCodes.TooManyRequests, httpStatusCode: 429);
        }

        var planCode = string.IsNullOrWhiteSpace(request.PlanCode) ? options.DefaultPlanCode : request.PlanCode.Trim().ToLowerInvariant();
        if (!await catalog.Plans.AnyAsync(p => p.Code == planCode && p.IsPublic && p.IsActive, cancellationToken))
        {
            return PlatformResults.Fail<SubmitSignupDto>("The selected plan is not available.", ApiErrorCodes.ValidationError, httpStatusCode: 400);
        }

        if (await SignupKeyAvailability.UnavailableReasonAsync(catalog, tenancyOptions.Value, key, now, cancellationToken) is not null)
        {
            return PlatformResults.Fail<SubmitSignupDto>(KeyUnavailableMessage, ApiErrorCodes.TenantKeyUnavailable, httpStatusCode: 409);
        }

        var token = SignupTokens.Generate();
        var signup = SignupRequest.Create(
            key,
            request.OrganizationName,
            request.AdminFullName,
            email,
            request.AdminPhone,
            planCode,
            request.Language,
            request.DatabaseMode == SignupDatabaseMode.BringYourOwn ? TenantDatabaseMode.BringYourOwn : TenantDatabaseMode.Managed,
            SignupTokens.Hash(token),
            now.AddHours(options.SignupEmailTokenHours),
            ip,
            options.RequireSignupApproval,
            now);

        catalog.SignupRequests.Add(signup);
        audit.Add(PlatformAuditActions.SignupSubmitted, null, nameof(SignupRequest), signup.Id.ToString(), $"{key} ({signup.DatabaseMode})");
        await catalog.SaveChangesAsync(cancellationToken);

        await SignupVerificationMail.SendAsync(emailService, options, signup, token, logger, cancellationToken);

        return Result<SubmitSignupDto>.Success(new SubmitSignupDto(signup.Id, signup.AdminEmail));
    }
}

/// <summary>Sends the confirmation link. A send failure is logged, not surfaced: the page offers a resend.</summary>
internal static class SignupVerificationMail
{
    public static async Task SendAsync(IEmailService emailService, PlatformOptions options, SignupRequest signup, string token,
        ILogger logger, CancellationToken cancellationToken)
    {
        var link = $"{options.PublicAppBaseUrl.TrimEnd('/')}/signup/verify?id={signup.Id}&token={Uri.EscapeDataString(token)}";
        var mail = SignupEmails.Verification(signup.Language, signup.OrganizationName, link, options.SignupEmailTokenHours);

        await SignupNoticeMail.SendAsync(emailService, signup, mail, logger, cancellationToken);
    }
}

/// <summary>
/// Sends a signup notice to the customer. A send failure is logged, not surfaced: the notice only
/// mirrors what the status page and the console already show.
/// </summary>
internal static class SignupNoticeMail
{
    public static async Task SendAsync(IEmailService emailService, SignupRequest signup, SignupEmail mail, ILogger logger,
        CancellationToken cancellationToken)
    {
        try
        {
            await emailService.SendAsync(signup.AdminEmail, mail.Subject, mail.HtmlBody, SignupEmails.SenderName, cancellationToken: cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Sending the signup e-mail '{Subject}' for {SignupId} failed.", mail.Subject, signup.Id);
        }
    }
}
