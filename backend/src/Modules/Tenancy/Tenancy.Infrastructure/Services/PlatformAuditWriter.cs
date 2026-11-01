using Microsoft.AspNetCore.Http;
using NWFM.Shared.Abstractions;
using NWFM.Shared.Http;
using Tenancy.Application.Common.Interfaces;
using Tenancy.Domain.Entities;

namespace Tenancy.Infrastructure.Services;

internal sealed class PlatformAuditWriter(
    ICatalogDbContext catalog,
    IPlatformUser platformUser,
    ICurrentUser tenantUser,
    IHttpContextAccessor httpContextAccessor,
    TimeProvider timeProvider)
    : IPlatformAuditWriter
{
    private const string SystemActor = "system";

    public void Add(string action, Guid? tenantId, string? targetType = null, string? targetId = null, string? details = null)
    {
        var (actorId, actorName) = platformUser.IsAuthenticated
            ? (platformUser.Id?.ToString(), platformUser.Email)
            : tenantUser.Id is { } userId
                ? (userId, tenantUser.UserName)
                : (null, SystemActor);

        var httpContext = httpContextAccessor.HttpContext;
        var ip = httpContext is null ? null : ClientIpResolver.Resolve(httpContext, trustForwardedHeader: false);

        catalog.PlatformAuditEntries.Add(PlatformAuditEntry.Create(
            action, actorId, actorName, tenantId, targetType, targetId, details, ip, timeProvider.GetUtcNow().UtcDateTime));
    }
}
