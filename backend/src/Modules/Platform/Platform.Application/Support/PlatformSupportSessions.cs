using Microsoft.Extensions.Options;
using NWFM.Shared.Options;
using Platform.Application.Common.Interfaces;
using Platform.Application.Common.Security;
using Tenancy.Application.Common.Interfaces;
using Tenancy.Application.SupportAccess.Models;

namespace Platform.Application.Support;

[PlatformAuthorize]
public sealed record GetPlatformSupportSessionsQuery : IRequest<Result<IReadOnlyList<SupportAccessSessionDto>>>
{
    public Guid TenantId { get; init; }
}

public sealed class GetPlatformSupportSessionsQueryValidator : AbstractValidator<GetPlatformSupportSessionsQuery>
{
    public GetPlatformSupportSessionsQueryValidator()
    {
        RuleFor(x => x.TenantId).NotEmpty().WithMessage("Workspace id is required.");
    }
}

public sealed class GetPlatformSupportSessionsQueryHandler(ICatalogDbContext catalog, TimeProvider timeProvider)
    : IRequestHandler<GetPlatformSupportSessionsQuery, Result<IReadOnlyList<SupportAccessSessionDto>>>
{
    public async Task<Result<IReadOnlyList<SupportAccessSessionDto>>> Handle(
        GetPlatformSupportSessionsQuery request,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var sessions = await catalog.SupportSessions
            .AsNoTracking()
            .Where(s => s.TenantId == request.TenantId)
            .OrderByDescending(s => s.StartedAtUtc)
            .Take(100)
            .ToListAsync(cancellationToken);

        return Result<IReadOnlyList<SupportAccessSessionDto>>.Success(sessions.Select(s => s.ToDto(now)).ToList());
    }
}

[PlatformAuthorize]
public sealed record GetPlatformSupportGrantsQuery : IRequest<Result<IReadOnlyList<SupportAccessGrantDto>>>
{
    public Guid TenantId { get; init; }
}

public sealed class GetPlatformSupportGrantsQueryValidator : AbstractValidator<GetPlatformSupportGrantsQuery>
{
    public GetPlatformSupportGrantsQueryValidator()
    {
        RuleFor(x => x.TenantId).NotEmpty().WithMessage("Workspace id is required.");
    }
}

public sealed class GetPlatformSupportGrantsQueryHandler(ICatalogDbContext catalog, TimeProvider timeProvider)
    : IRequestHandler<GetPlatformSupportGrantsQuery, Result<IReadOnlyList<SupportAccessGrantDto>>>
{
    public async Task<Result<IReadOnlyList<SupportAccessGrantDto>>> Handle(
        GetPlatformSupportGrantsQuery request,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var grants = await catalog.SupportAccessGrants
            .AsNoTracking()
            .Where(g => g.TenantId == request.TenantId)
            .OrderByDescending(g => g.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        return Result<IReadOnlyList<SupportAccessGrantDto>>.Success(grants.Select(g => g.ToDto(now)).ToList());
    }
}

[PlatformAuthorize]
public sealed record GetPlatformTenantUsersQuery : IRequest<Result<IReadOnlyList<SupportImpersonationCandidateDto>>>
{
    public Guid TenantId { get; init; }

    public string? SearchTerm { get; init; }
}

public sealed class GetPlatformTenantUsersQueryValidator : AbstractValidator<GetPlatformTenantUsersQuery>
{
    public GetPlatformTenantUsersQueryValidator()
    {
        RuleFor(x => x.TenantId).NotEmpty().WithMessage("Workspace id is required.");
    }
}

public sealed class GetPlatformTenantUsersQueryHandler(ITenantStore tenantStore, ISupportTenantDirectory directory)
    : IRequestHandler<GetPlatformTenantUsersQuery, Result<IReadOnlyList<SupportImpersonationCandidateDto>>>
{
    public Task<Result<IReadOnlyList<SupportImpersonationCandidateDto>>> Handle(
        GetPlatformTenantUsersQuery request,
        CancellationToken cancellationToken)
    {
        var tenant = tenantStore.FindById(request.TenantId);
        return tenant is null
            ? Task.FromResult(PlatformResults.Fail<IReadOnlyList<SupportImpersonationCandidateDto>>(
                "Workspace not found.", ApiErrorCodes.TenantNotFound, httpStatusCode: 404))
            : directory.ListCandidatesAsync(tenant, request.SearchTerm, cancellationToken);
    }
}

[PlatformAuthorize]
public sealed record StartSupportSessionCommand : IRequest<Result<SupportSessionHandoffDto>>
{
    public Guid TenantId { get; init; }

    public string TicketReference { get; init; } = default!;

    /// <summary>Tenant account to act as. Empty picks the workspace administrator.</summary>
    public string? ImpersonatedUserId { get; init; }
}

public sealed class StartSupportSessionCommandValidator : AbstractValidator<StartSupportSessionCommand>
{
    public StartSupportSessionCommandValidator()
    {
        RuleFor(x => x.TenantId).NotEmpty().WithMessage("Workspace id is required.");
        RuleFor(x => x.TicketReference)
            .NotEmpty().WithMessage("A support session must reference a ticket.")
            .MaximumLength(SupportSession.TicketMaxLength)
            .WithMessage($"Ticket reference must not exceed {SupportSession.TicketMaxLength} characters.");
    }
}

public sealed class StartSupportSessionCommandHandler(
    ICatalogDbContext catalog,
    ITenantStore tenantStore,
    IPlatformUser platformUser,
    ISupportTenantDirectory directory,
    IPlatformAuditWriter audit,
    IOptions<PlatformOptions> platformOptions,
    TimeProvider timeProvider)
    : IRequestHandler<StartSupportSessionCommand, Result<SupportSessionHandoffDto>>
{
    private const string KeyPlaceholder = "{key}";
    private const string HandoffPath = "/auth/support#code=";

    public async Task<Result<SupportSessionHandoffDto>> Handle(
        StartSupportSessionCommand request,
        CancellationToken cancellationToken)
    {
        var tenant = tenantStore.FindById(request.TenantId);
        if (tenant is null)
        {
            return PlatformResults.Fail<SupportSessionHandoffDto>("Workspace not found.", ApiErrorCodes.TenantNotFound, httpStatusCode: 404);
        }

        if (tenant.Status != TenantStatus.Active)
        {
            return PlatformResults.Fail<SupportSessionHandoffDto>(
                "Support can only enter an active workspace.",
                ApiErrorCodes.TenantSuspended,
                httpStatusCode: 403);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var grant = await catalog.SupportAccessGrants
            .Where(g => g.TenantId == tenant.Id && g.RevokedAtUtc == null && g.ExpiresAtUtc > now)
            .OrderByDescending(g => g.ExpiresAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (grant is null)
        {
            return PlatformResults.Fail<SupportSessionHandoffDto>(
                "The workspace has not granted support access, or the grant has ended.",
                ApiErrorCodes.SupportAccessNotGranted,
                httpStatusCode: 403);
        }

        var staff = await catalog.PlatformUsers.FirstOrDefaultAsync(u => u.Id == platformUser.Id, cancellationToken);
        if (staff is null || !staff.IsActive)
        {
            return PlatformResults.Fail<SupportSessionHandoffDto>(
                "Platform staff access is required.",
                ApiErrorCodes.UnauthorizedAccess,
                httpStatusCode: 401);
        }

        var candidate = await directory.ResolveUserAsync(tenant, request.ImpersonatedUserId, cancellationToken);
        if (!candidate.IsSuccess || candidate.Value is null)
        {
            return Result.Failure<SupportSessionHandoffDto>(candidate.Error);
        }

        var session = SupportSession.Start(
            grant,
            staff,
            request.TicketReference,
            candidate.Value.UserId,
            candidate.Value.UserName,
            platformOptions.Value.SupportSessionMinutes,
            now);

        var options = platformOptions.Value;
        var code = session.IssueHandoffCode(TimeSpan.FromSeconds(options.SupportHandoffSeconds), now);

        catalog.SupportSessions.Add(session);
        audit.Add(PlatformAuditActions.SupportSessionStarted, tenant.Id, nameof(SupportSession), session.Id.ToString(),
            $"{staff.DisplayName} as {candidate.Value.UserName} ({session.Scope}) ticket {session.TicketReference}");
        await catalog.SaveChangesAsync(cancellationToken);

        var tenantUrl = options.TenantAppUrlTemplate.Replace(KeyPlaceholder, tenant.Key, StringComparison.Ordinal).TrimEnd('/');

        return Result<SupportSessionHandoffDto>.Success(new SupportSessionHandoffDto
        {
            SessionId = session.Id,
            TenantId = tenant.Id,
            TenantKey = tenant.Key,
            HandoffUrl = tenantUrl + HandoffPath + Uri.EscapeDataString(code),
            HandoffExpiresAtUtc = session.HandoffExpiresAtUtc!.Value,
            SessionExpiresAtUtc = session.ExpiresAtUtc
        });
    }
}

[PlatformAuthorize]
public sealed record EndSupportSessionCommand : IRequest<Result<bool>>
{
    public Guid SessionId { get; init; }
}

public sealed class EndSupportSessionCommandValidator : AbstractValidator<EndSupportSessionCommand>
{
    public EndSupportSessionCommandValidator()
    {
        RuleFor(x => x.SessionId).NotEmpty().WithMessage("Session id is required.");
    }
}

public sealed class EndSupportSessionCommandHandler(
    ICatalogDbContext catalog,
    IPlatformAuditWriter audit,
    TimeProvider timeProvider)
    : IRequestHandler<EndSupportSessionCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(EndSupportSessionCommand request, CancellationToken cancellationToken)
    {
        var session = await catalog.SupportSessions.FirstOrDefaultAsync(s => s.Id == request.SessionId, cancellationToken);
        if (session is null)
        {
            return PlatformResults.Fail<bool>("Support session not found.", ApiErrorCodes.NotFound, httpStatusCode: 404);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        session.End(now);
        audit.Add(PlatformAuditActions.SupportSessionEnded, session.TenantId, nameof(SupportSession), session.Id.ToString());
        await catalog.SaveChangesAsync(cancellationToken);

        return Result<bool>.Success(true);
    }
}
