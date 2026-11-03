using Platform.Application.Common.Security;
using Tenancy.Application.Common.Interfaces;

namespace Platform.Application.Audit;

public sealed record PlatformAuditEntryDto
{
    public long Id { get; init; }

    public DateTime AtUtc { get; init; }

    public string? ActorId { get; init; }

    public string? ActorName { get; init; }

    public string Action { get; init; } = default!;

    public Guid? TenantId { get; init; }

    public string? TenantKey { get; init; }

    public string? TargetType { get; init; }

    public string? TargetId { get; init; }

    public string? Details { get; init; }

    public string? IpAddress { get; init; }
}

/// <summary>Platform audit trail, newest first.</summary>
[PlatformAuthorize]
public sealed record GetPlatformAuditQuery : IRequest<Result<PaginatedResult<PlatformAuditEntryDto>>>
{
    public int PageNumber { get; init; } = 1;

    public int PageSize { get; init; } = 50;

    public Guid? TenantId { get; init; }

    /// <summary>Exact action, or a prefix ending in '.' (for example <c>support.</c>).</summary>
    public string? Action { get; init; }

    /// <summary>Matches actor name, target id or details.</summary>
    public string? SearchTerm { get; init; }

    public DateTime? FromUtc { get; init; }

    public DateTime? ToUtc { get; init; }
}

public sealed class GetPlatformAuditQueryValidator : AbstractValidator<GetPlatformAuditQuery>
{
    public GetPlatformAuditQueryValidator()
    {
        RuleFor(x => x.PageNumber).GreaterThan(0).WithMessage("Page number must be greater than 0.");
        RuleFor(x => x.PageSize).InclusiveBetween(1, 200).WithMessage("Page size must be between 1 and 200.");
        RuleFor(x => x.ToUtc)
            .GreaterThanOrEqualTo(x => x.FromUtc).WithMessage("The end of the range must not be before its start.")
            .When(x => x.FromUtc.HasValue && x.ToUtc.HasValue);
    }
}

public sealed class GetPlatformAuditQueryHandler(ICatalogDbContext catalog)
    : IRequestHandler<GetPlatformAuditQuery, Result<PaginatedResult<PlatformAuditEntryDto>>>
{
    private const string ActionPrefixMarker = ".";

    public async Task<Result<PaginatedResult<PlatformAuditEntryDto>>> Handle(GetPlatformAuditQuery request, CancellationToken cancellationToken)
    {
        IQueryable<PlatformAuditEntry> entries = catalog.PlatformAuditEntries.AsNoTracking();

        if (request.TenantId is { } tenantId)
        {
            entries = entries.Where(e => e.TenantId == tenantId);
        }

        if (!string.IsNullOrWhiteSpace(request.Action))
        {
            var action = request.Action.Trim();
            entries = action.EndsWith(ActionPrefixMarker, StringComparison.Ordinal)
                ? entries.Where(e => e.Action.StartsWith(action))
                : entries.Where(e => e.Action == action);
        }

        if (!string.IsNullOrWhiteSpace(request.SearchTerm))
        {
            var term = request.SearchTerm.Trim();
            entries = entries.Where(e =>
                (e.ActorName != null && e.ActorName.Contains(term)) ||
                (e.TargetId != null && e.TargetId.Contains(term)) ||
                (e.Details != null && e.Details.Contains(term)));
        }

        if (request.FromUtc is { } from)
        {
            entries = entries.Where(e => e.AtUtc >= from);
        }

        if (request.ToUtc is { } to)
        {
            entries = entries.Where(e => e.AtUtc <= to);
        }

        var total = await entries.CountAsync(cancellationToken);

        var items = await entries
            .OrderByDescending(e => e.Id)
            .Skip((request.PageNumber - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(e => new PlatformAuditEntryDto
            {
                Id = e.Id,
                AtUtc = e.AtUtc,
                ActorId = e.ActorId,
                ActorName = e.ActorName,
                Action = e.Action,
                TenantId = e.TenantId,
                TenantKey = e.TenantId == null ? null : catalog.Tenants.Where(t => t.Id == e.TenantId).Select(t => t.Key).FirstOrDefault(),
                TargetType = e.TargetType,
                TargetId = e.TargetId,
                Details = e.Details,
                IpAddress = e.IpAddress
            })
            .ToListAsync(cancellationToken);

        return Result<PaginatedResult<PlatformAuditEntryDto>>.Success(
            new PaginatedResult<PlatformAuditEntryDto>(items, total, request.PageNumber, request.PageSize));
    }
}
