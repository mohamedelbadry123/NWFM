using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NWFM.Shared.Messaging;
using NWFM.Shared.Options;
using Platform.Application.Common.Interfaces;
using Platform.Application.Signup.Common;
using Tenancy.Application.Common.Interfaces;

namespace Platform.Application.Signup.Public;

/// <summary>What the signup page shows while the workspace is created. Carries no contact details.</summary>
public sealed record SignupStatusDto
{
    public Guid SignupId { get; init; }

    public string TenantKey { get; init; } = default!;

    public string OrganizationName { get; init; } = default!;

    public string Status { get; init; } = default!;

    public string DatabaseMode { get; init; } = default!;

    public bool IsEmailVerified { get; init; }

    public string? LastCompletedStep { get; init; }

    public int CompletedSteps { get; init; }

    public int TotalSteps { get; init; }

    /// <summary>
    /// Why a bring-your-own database is not accepted yet — meant for the customer. Other failures
    /// stay internal and show as a generic message.
    /// </summary>
    public string? Message { get; init; }

    /// <summary>Set once the workspace is live.</summary>
    public string? WorkspaceUrl { get; init; }
}

internal static class SignupStatusMapper
{
    private const string GenericFailure = "We could not finish creating the workspace. Our team has been notified and will follow up.";
    private const string AwaitingApprovalMessage = "Your request is being reviewed. You will receive your sign-in details by e-mail once it is approved.";
    private const string RejectedMessage = "We could not accept this request. Please contact our team if you have any questions.";
    private const string KeyPlaceholder = "{key}";

    public static SignupStatusDto ToStatusDto(this SignupRequest signup, PlatformOptions options) => new()
    {
        SignupId = signup.Id,
        TenantKey = signup.TenantKey,
        OrganizationName = signup.OrganizationName,
        Status = signup.Status.ToString(),
        DatabaseMode = signup.DatabaseMode.ToString(),
        IsEmailVerified = signup.IsEmailVerified,
        LastCompletedStep = signup.LastCompletedStep?.ToString(),
        CompletedSteps = signup.LastCompletedStep is { } step ? (int)step : 0,
        TotalSteps = Enum.GetValues<ProvisioningStep>().Length,
        Message = signup.Status switch
        {
            SignupStatus.AwaitingDatabase => signup.LastError,
            SignupStatus.Failed => GenericFailure,
            SignupStatus.AwaitingApproval => AwaitingApprovalMessage,
            SignupStatus.Rejected => RejectedMessage,
            _ => null
        },
        WorkspaceUrl = signup.Status == SignupStatus.Completed ? WorkspaceUrl(options, signup.TenantKey) : null
    };

    public static string WorkspaceUrl(PlatformOptions options, string tenantKey) =>
        options.TenantAppUrlTemplate.Replace(KeyPlaceholder, tenantKey, StringComparison.Ordinal);
}

public sealed record GetSignupStatusQuery(Guid SignupId) : IRequest<Result<SignupStatusDto>>;

public sealed class GetSignupStatusQueryHandler(ICatalogDbContext catalog, IOptions<PlatformOptions> platformOptions)
    : IRequestHandler<GetSignupStatusQuery, Result<SignupStatusDto>>
{
    public async Task<Result<SignupStatusDto>> Handle(GetSignupStatusQuery request, CancellationToken cancellationToken)
    {
        var signup = await catalog.SignupRequests.AsNoTracking().FirstOrDefaultAsync(s => s.Id == request.SignupId, cancellationToken);

        return signup is null
            ? PlatformResults.Fail<SignupStatusDto>("Signup not found.", ApiErrorCodes.NotFound, httpStatusCode: 404)
            : Result<SignupStatusDto>.Success(signup.ToStatusDto(platformOptions.Value));
    }
}

/// <summary>
/// The link in the confirmation e-mail. When approval is required the request waits for staff;
/// otherwise a managed signup is queued for provisioning at once and a bring-your-own one moves on
/// to the database step.
/// </summary>
public sealed record VerifySignupEmailCommand(Guid SignupId, string Token) : IRequest<Result<SignupStatusDto>>;

public sealed class VerifySignupEmailCommandValidator : AbstractValidator<VerifySignupEmailCommand>
{
    public VerifySignupEmailCommandValidator()
    {
        RuleFor(x => x.SignupId).NotEmpty().WithMessage("Signup id is required.");
        RuleFor(x => x.Token).NotEmpty().WithMessage("Confirmation token is required.");
    }
}

public sealed class VerifySignupEmailCommandHandler(
    ICatalogDbContext catalog,
    ISignupProvisioningQueue provisioningQueue,
    IEmailService emailService,
    IOptions<PlatformOptions> platformOptions,
    TimeProvider timeProvider,
    ILogger<VerifySignupEmailCommandHandler> logger)
    : IRequestHandler<VerifySignupEmailCommand, Result<SignupStatusDto>>
{
    private const string InvalidLink = "This confirmation link is not valid or has expired.";
    private const string EnterDatabase = "Enter the connection details of your database.";

    public async Task<Result<SignupStatusDto>> Handle(VerifySignupEmailCommand request, CancellationToken cancellationToken)
    {
        var signup = await catalog.SignupRequests.FirstOrDefaultAsync(s => s.Id == request.SignupId, cancellationToken);

        if (signup is null)
        {
            return PlatformResults.Fail<SignupStatusDto>(InvalidLink, ApiErrorCodes.ValidationError, httpStatusCode: 400);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;

        if (!signup.VerifyEmail(SignupTokens.Hash(request.Token), now))
        {
            // Persist the failed attempt: the counter is what stops a token being guessed.
            await catalog.SaveChangesAsync(cancellationToken);
            return PlatformResults.Fail<SignupStatusDto>(InvalidLink, ApiErrorCodes.ValidationError, httpStatusCode: 400);
        }

        // Clicking the link again must not queue a second run.
        var queue = false;
        var awaitingApproval = false;
        if (signup.Status == SignupStatus.PendingEmailVerification)
        {
            if (signup.IsAwaitingApprovalGate)
            {
                signup.AwaitApproval(now);
                awaitingApproval = true;
            }
            else if (signup.DatabaseMode == TenantDatabaseMode.BringYourOwn && !signup.HasBringYourOwnDatabase)
            {
                signup.AwaitDatabase(EnterDatabase, now);
            }
            else
            {
                signup.Queue(now);
                queue = true;
            }
        }

        await catalog.SaveChangesAsync(cancellationToken);

        if (queue)
        {
            provisioningQueue.Enqueue(signup.Id);
        }

        if (awaitingApproval)
        {
            await SignupNoticeMail.SendAsync(emailService, signup,
                SignupEmails.AwaitingApproval(signup.Language, signup.OrganizationName), logger, cancellationToken);
        }

        return Result<SignupStatusDto>.Success(signup.ToStatusDto(platformOptions.Value));
    }
}

public sealed record ResendSignupVerificationCommand(Guid SignupId) : IRequest<Result<bool>>;

public sealed class ResendSignupVerificationCommandHandler(
    ICatalogDbContext catalog,
    IEmailService emailService,
    IOptions<PlatformOptions> platformOptions,
    TimeProvider timeProvider,
    ILogger<ResendSignupVerificationCommandHandler> logger)
    : IRequestHandler<ResendSignupVerificationCommand, Result<bool>>
{
    private static readonly TimeSpan ResendInterval = TimeSpan.FromMinutes(1);

    public async Task<Result<bool>> Handle(ResendSignupVerificationCommand request, CancellationToken cancellationToken)
    {
        var signup = await catalog.SignupRequests.FirstOrDefaultAsync(s => s.Id == request.SignupId, cancellationToken);

        if (signup is null)
        {
            return PlatformResults.Fail<bool>("Signup not found.", ApiErrorCodes.NotFound, httpStatusCode: 404);
        }

        if (signup.IsEmailVerified || signup.Status != SignupStatus.PendingEmailVerification)
        {
            return PlatformResults.Fail<bool>("The e-mail address is already confirmed.", ApiErrorCodes.SignupInvalidState, httpStatusCode: 409);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        if (now - signup.UpdatedAtUtc < ResendInterval)
        {
            return PlatformResults.Fail<bool>("Please wait a minute before asking for another e-mail.", ApiErrorCodes.TooManyRequests, httpStatusCode: 429);
        }

        var options = platformOptions.Value;
        var token = SignupTokens.Generate();
        signup.ReissueEmailVerification(SignupTokens.Hash(token), now.AddHours(options.SignupEmailTokenHours), now);
        await catalog.SaveChangesAsync(cancellationToken);

        await SignupVerificationMail.SendAsync(emailService, options, signup, token, logger, cancellationToken);
        return Result<bool>.Success(true);
    }
}
