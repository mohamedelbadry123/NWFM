using Microsoft.Extensions.Logging;
using NWFM.Shared.Messaging;
using Platform.Application.Common.Interfaces;
using Platform.Application.Common.Security;
using Platform.Application.Signup.Common;
using Platform.Application.Signup.Public;
using Tenancy.Application.Common.Interfaces;
using Tenancy.Domain.Constants;

namespace Platform.Application.Signup.Admin;

public sealed record PlatformSignupEventDto(string Step, bool Succeeded, string? Message, DateTime AtUtc);

public sealed record PlatformSignupDto
{
    public Guid Id { get; init; }

    public string TenantKey { get; init; } = default!;

    public string OrganizationName { get; init; } = default!;

    public string AdminFullName { get; init; } = default!;

    public string AdminEmail { get; init; } = default!;

    public string? AdminPhone { get; init; }

    public string PlanCode { get; init; } = default!;

    public string Language { get; init; } = default!;

    public string DatabaseMode { get; init; } = default!;

    /// <summary>Bring-your-own server host; the credentials are never returned.</summary>
    public string? ByoServer { get; init; }

    public string? ByoDatabase { get; init; }

    public string Status { get; init; } = default!;

    public bool IsEmailVerified { get; init; }

    public bool ApprovalRequired { get; init; }

    public DateTime? ApprovedAtUtc { get; init; }

    public string? ApprovedBy { get; init; }

    public string? LastCompletedStep { get; init; }

    public string? LastError { get; init; }

    public int ProvisioningAttempts { get; init; }

    public Guid? TenantId { get; init; }

    public string? ClientIp { get; init; }

    public DateTime CreatedAtUtc { get; init; }

    public DateTime UpdatedAtUtc { get; init; }

    public DateTime? CompletedAtUtc { get; init; }

    public IReadOnlyList<PlatformSignupEventDto> Events { get; init; } = [];
}

[PlatformAuthorize]
public sealed record GetPlatformSignupsQuery : IRequest<Result<PaginatedResult<PlatformSignupDto>>>
{
    public int PageNumber { get; init; } = 1;

    public int PageSize { get; init; } = 20;

    public SignupStatus? Status { get; init; }

    /// <summary>Matches key, organisation or e-mail.</summary>
    public string? SearchTerm { get; init; }
}

public sealed class GetPlatformSignupsQueryValidator : AbstractValidator<GetPlatformSignupsQuery>
{
    public GetPlatformSignupsQueryValidator()
    {
        RuleFor(x => x.PageNumber).GreaterThan(0).WithMessage("Page number must be greater than 0.");
        RuleFor(x => x.PageSize).InclusiveBetween(1, 200).WithMessage("Page size must be between 1 and 200.");
    }
}

public sealed class GetPlatformSignupsQueryHandler(ICatalogDbContext catalog)
    : IRequestHandler<GetPlatformSignupsQuery, Result<PaginatedResult<PlatformSignupDto>>>
{
    public async Task<Result<PaginatedResult<PlatformSignupDto>>> Handle(GetPlatformSignupsQuery request, CancellationToken cancellationToken)
    {
        IQueryable<SignupRequest> signups = catalog.SignupRequests.AsNoTracking();

        if (request.Status is { } status)
        {
            signups = signups.Where(s => s.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(request.SearchTerm))
        {
            var term = request.SearchTerm.Trim();
            signups = signups.Where(s => s.TenantKey.Contains(term) || s.OrganizationName.Contains(term) || s.AdminEmail.Contains(term));
        }

        var total = await signups.CountAsync(cancellationToken);

        var items = await signups
            .OrderByDescending(s => s.CreatedAtUtc)
            .Skip((request.PageNumber - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(s => new PlatformSignupDto
            {
                Id = s.Id,
                TenantKey = s.TenantKey,
                OrganizationName = s.OrganizationName,
                AdminFullName = s.AdminFullName,
                AdminEmail = s.AdminEmail,
                AdminPhone = s.AdminPhone,
                PlanCode = s.PlanCode,
                Language = s.Language,
                DatabaseMode = s.DatabaseMode.ToString(),
                ByoServer = s.ByoServer,
                ByoDatabase = s.ByoDatabase,
                Status = s.Status.ToString(),
                IsEmailVerified = s.EmailVerifiedAtUtc != null,
                ApprovalRequired = s.ApprovalRequired,
                ApprovedAtUtc = s.ApprovedAtUtc,
                ApprovedBy = s.ApprovedBy,
                LastCompletedStep = s.LastCompletedStep == null ? null : s.LastCompletedStep.ToString(),
                LastError = s.LastError,
                ProvisioningAttempts = s.ProvisioningAttempts,
                TenantId = s.TenantId,
                ClientIp = s.ClientIp,
                CreatedAtUtc = s.CreatedAtUtc,
                UpdatedAtUtc = s.UpdatedAtUtc,
                CompletedAtUtc = s.CompletedAtUtc,
                Events = s.Events
                    .OrderBy(e => e.Id)
                    .Select(e => new PlatformSignupEventDto(e.Step.ToString(), e.Succeeded, e.Message, e.AtUtc))
                    .ToList()
            })
            .ToListAsync(cancellationToken);

        return Result<PaginatedResult<PlatformSignupDto>>.Success(
            new PaginatedResult<PlatformSignupDto>(items, total, request.PageNumber, request.PageSize));
    }
}

/// <summary>
/// Runs provisioning again from the last finished step: after a failure, or when a run has been
/// stuck in <c>Provisioning</c> long enough that its process clearly died.
/// </summary>
[PlatformAuthorize(Roles = PlatformRoles.Admin)]
public sealed record RetryPlatformSignupCommand(Guid SignupId) : IRequest<Result<bool>>;

public sealed class RetryPlatformSignupCommandHandler(
    ICatalogDbContext catalog,
    ISignupProvisioningQueue provisioningQueue,
    IPlatformAuditWriter audit,
    TimeProvider timeProvider)
    : IRequestHandler<RetryPlatformSignupCommand, Result<bool>>
{
    private static readonly TimeSpan StuckAfter = TimeSpan.FromMinutes(15);

    public async Task<Result<bool>> Handle(RetryPlatformSignupCommand request, CancellationToken cancellationToken)
    {
        var signup = await catalog.SignupRequests.FirstOrDefaultAsync(s => s.Id == request.SignupId, cancellationToken);

        if (signup is null)
        {
            return PlatformResults.Fail<bool>("Signup not found.", ApiErrorCodes.NotFound, httpStatusCode: 404);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var stuck = signup.Status == SignupStatus.Provisioning && now - signup.UpdatedAtUtc > StuckAfter;
        var retryable = signup.Status is SignupStatus.Failed or SignupStatus.Queued
            || (signup.Status == SignupStatus.AwaitingDatabase && signup.HasBringYourOwnDatabase);

        if (!stuck && !retryable)
        {
            return PlatformResults.Fail<bool>($"A signup in state '{signup.Status}' cannot be retried.", ApiErrorCodes.SignupInvalidState, httpStatusCode: 409);
        }

        if (retryable && signup.Status != SignupStatus.Queued)
        {
            signup.Queue(now);
        }

        audit.Add(PlatformAuditActions.SignupRetried, signup.TenantId, nameof(SignupRequest), signup.Id.ToString(), $"Attempt {signup.ProvisioningAttempts + 1}");
        await catalog.SaveChangesAsync(cancellationToken);

        provisioningQueue.Enqueue(signup.Id);
        return Result<bool>.Success(true);
    }
}

[PlatformAuthorize(Roles = PlatformRoles.Admin)]
public sealed record RejectPlatformSignupCommand : IRequest<Result<bool>>
{
    public Guid SignupId { get; init; }

    public string Reason { get; init; } = default!;
}

public sealed class RejectPlatformSignupCommandValidator : AbstractValidator<RejectPlatformSignupCommand>
{
    public RejectPlatformSignupCommandValidator()
    {
        RuleFor(x => x.SignupId).NotEmpty().WithMessage("Signup id is required.");
        RuleFor(x => x.Reason)
            .NotEmpty().WithMessage("A reason is required.")
            .MaximumLength(SignupRequest.ErrorMaxLength).WithMessage($"Reason must not exceed {SignupRequest.ErrorMaxLength} characters.");
    }
}

public sealed class RejectPlatformSignupCommandHandler(
    ICatalogDbContext catalog,
    IPlatformAuditWriter audit,
    IEmailService emailService,
    TimeProvider timeProvider,
    ILogger<RejectPlatformSignupCommandHandler> logger)
    : IRequestHandler<RejectPlatformSignupCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(RejectPlatformSignupCommand request, CancellationToken cancellationToken)
    {
        var signup = await catalog.SignupRequests.FirstOrDefaultAsync(s => s.Id == request.SignupId, cancellationToken);

        if (signup is null)
        {
            return PlatformResults.Fail<bool>("Signup not found.", ApiErrorCodes.NotFound, httpStatusCode: 404);
        }

        if (signup.Status is SignupStatus.Completed or SignupStatus.Provisioning)
        {
            return PlatformResults.Fail<bool>("A signup that is provisioning or complete cannot be rejected.", ApiErrorCodes.SignupInvalidState, httpStatusCode: 409);
        }

        signup.Reject(request.Reason.Trim(), timeProvider.GetUtcNow().UtcDateTime);
        audit.Add(PlatformAuditActions.SignupRejected, signup.TenantId, nameof(SignupRequest), signup.Id.ToString(), request.Reason.Trim());
        await catalog.SaveChangesAsync(cancellationToken);

        // Only a customer who confirmed the address hears back: an unconfirmed one may not own it.
        if (signup.IsEmailVerified)
        {
            await SignupNoticeMail.SendAsync(emailService, signup,
                SignupEmails.Rejected(signup.Language, signup.OrganizationName), logger, cancellationToken);
        }

        return Result<bool>.Success(true);
    }
}

/// <summary>
/// Staff accept a confirmed self-service signup. A managed one is queued for provisioning at once; a
/// bring-your-own one goes on to the database step unless its details are already in.
/// </summary>
[PlatformAuthorize(Roles = PlatformRoles.Admin)]
public sealed record ApprovePlatformSignupCommand(Guid SignupId) : IRequest<Result<bool>>;

public sealed class ApprovePlatformSignupCommandValidator : AbstractValidator<ApprovePlatformSignupCommand>
{
    public ApprovePlatformSignupCommandValidator()
    {
        RuleFor(x => x.SignupId).NotEmpty().WithMessage("Signup id is required.");
    }
}

public sealed class ApprovePlatformSignupCommandHandler(
    ICatalogDbContext catalog,
    ISignupProvisioningQueue provisioningQueue,
    IPlatformAuditWriter audit,
    IPlatformUser platformUser,
    TimeProvider timeProvider)
    : IRequestHandler<ApprovePlatformSignupCommand, Result<bool>>
{
    private const string UnknownApprover = "platform";

    public async Task<Result<bool>> Handle(ApprovePlatformSignupCommand request, CancellationToken cancellationToken)
    {
        var signup = await catalog.SignupRequests.FirstOrDefaultAsync(s => s.Id == request.SignupId, cancellationToken);

        if (signup is null)
        {
            return PlatformResults.Fail<bool>("Signup not found.", ApiErrorCodes.NotFound, httpStatusCode: 404);
        }

        if (signup.Status != SignupStatus.AwaitingApproval)
        {
            return PlatformResults.Fail<bool>($"A signup in state '{signup.Status}' cannot be approved.", ApiErrorCodes.SignupInvalidState, httpStatusCode: 409);
        }

        var approver = platformUser.Email ?? platformUser.DisplayName ?? UnknownApprover;
        signup.Approve(approver, timeProvider.GetUtcNow().UtcDateTime);
        audit.Add(PlatformAuditActions.SignupApproved, signup.TenantId, nameof(SignupRequest), signup.Id.ToString(), signup.TenantKey);
        await catalog.SaveChangesAsync(cancellationToken);

        if (signup.Status == SignupStatus.Queued)
        {
            provisioningQueue.Enqueue(signup.Id);
        }

        return Result<bool>.Success(true);
    }
}
