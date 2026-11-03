using Platform.Application.Common.Security;
using Platform.Application.Tenants.Models;
using Tenancy.Application.Common.Interfaces;

namespace Platform.Application.Tenants.Queries;

[PlatformAuthorize]
public sealed record GetPlatformTenantsQuery : IRequest<Result<PaginatedResult<PlatformTenantListItemDto>>>
{
    public int PageNumber { get; init; } = 1;

    public int PageSize { get; init; } = 20;

    /// <summary>Matches key, name or contact e-mail.</summary>
    public string? SearchTerm { get; init; }

    public TenantStatus? Status { get; init; }

    public string? SortField { get; init; }

    public bool SortDescending { get; init; }
}

public sealed class GetPlatformTenantsQueryValidator : AbstractValidator<GetPlatformTenantsQuery>
{
    public GetPlatformTenantsQueryValidator()
    {
        RuleFor(x => x.PageNumber).GreaterThan(0).WithMessage("Page number must be greater than 0.");
        RuleFor(x => x.PageSize).InclusiveBetween(1, 200).WithMessage("Page size must be between 1 and 200.");
    }
}

public sealed class GetPlatformTenantsQueryHandler(ICatalogDbContext catalog)
    : IRequestHandler<GetPlatformTenantsQuery, Result<PaginatedResult<PlatformTenantListItemDto>>>
{
    private const string SortByKey = "key";
    private const string SortByName = "name";
    private const string SortByStatus = "status";

    public async Task<Result<PaginatedResult<PlatformTenantListItemDto>>> Handle(
        GetPlatformTenantsQuery request,
        CancellationToken cancellationToken)
    {
        IQueryable<Tenant> tenants = catalog.Tenants.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(request.SearchTerm))
        {
            var term = request.SearchTerm.Trim();
            tenants = tenants.Where(t => t.Key.Contains(term) || t.Name.Contains(term) || t.ContactEmail.Contains(term));
        }

        if (request.Status is { } status)
        {
            tenants = tenants.Where(t => t.Status == status);
        }

        tenants = (request.SortField?.ToLowerInvariant(), request.SortDescending) switch
        {
            (SortByKey, false) => tenants.OrderBy(t => t.Key),
            (SortByKey, true) => tenants.OrderByDescending(t => t.Key),
            (SortByName, false) => tenants.OrderBy(t => t.Name),
            (SortByName, true) => tenants.OrderByDescending(t => t.Name),
            (SortByStatus, false) => tenants.OrderBy(t => t.Status),
            (SortByStatus, true) => tenants.OrderByDescending(t => t.Status),
            (_, false) => tenants.OrderBy(t => t.CreatedAtUtc),
            _ => tenants.OrderByDescending(t => t.CreatedAtUtc)
        };

        var total = await tenants.CountAsync(cancellationToken);

        var items = await tenants
            .Skip((request.PageNumber - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(t => new PlatformTenantListItemDto
            {
                Id = t.Id,
                Key = t.Key,
                Name = t.Name,
                Status = t.Status.ToString(),
                StatusReason = t.StatusReason,
                ContactEmail = t.ContactEmail,
                PlanCode = t.Subscription == null ? null : t.Subscription.Plan.Code,
                SubscriptionStatus = t.Subscription == null ? null : t.Subscription.Status.ToString(),
                TrialEndsAtUtc = t.Subscription == null ? null : t.Subscription.TrialEndsAtUtc,
                PeriodEndsAtUtc = t.Subscription == null ? null : t.Subscription.CurrentPeriodEndsAtUtc,
                DatabaseMode = t.Database == null ? null : t.Database.Mode.ToString(),
                SchemaVersion = t.Database == null ? null : t.Database.SchemaVersion,
                CreatedAtUtc = t.CreatedAtUtc
            })
            .ToListAsync(cancellationToken);

        return Result<PaginatedResult<PlatformTenantListItemDto>>.Success(
            new PaginatedResult<PlatformTenantListItemDto>(items, total, request.PageNumber, request.PageSize));
    }
}
