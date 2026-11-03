using Microsoft.Extensions.Options;
using NWFM.Shared.Options;
using Tenancy.Application.Common.Interfaces;
using Tenancy.Application.SupportAccess.Models;

namespace Tenancy.Application.SupportAccess.Queries.GetSupportAccess;

public sealed class GetSupportAccessQueryHandler(
    ICatalogDbContext catalog,
    ITenantContext tenantContext,
    IOptions<PlatformOptions> platformOptions,
    TimeProvider timeProvider)
    : IRequestHandler<GetSupportAccessQuery, Result<SupportAccessHistoryDto>>
{
    private const int MaxSessions = 100;

    public async Task<Result<SupportAccessHistoryDto>> Handle(GetSupportAccessQuery request, CancellationToken cancellationToken)
    {
        var tenant = tenantContext.Current ?? throw TenantAccessException.Required();
        var now = timeProvider.GetUtcNow().UtcDateTime;

        var grants = await catalog.SupportAccessGrants
            .AsNoTracking()
            .Where(g => g.TenantId == tenant.Id)
            .OrderByDescending(g => g.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        var sessions = await catalog.SupportSessions
            .AsNoTracking()
            .Where(s => s.TenantId == tenant.Id)
            .OrderByDescending(s => s.StartedAtUtc)
            .Take(MaxSessions)
            .ToListAsync(cancellationToken);

        return Result.Success(new SupportAccessHistoryDto
        {
            Grants = [.. grants.Select(g => g.ToDto(now))],
            Sessions = [.. sessions.Select(s => s.ToDto(now))],
            MaxGrantHours = platformOptions.Value.MaxSupportGrantHours
        });
    }
}
