using Microsoft.Extensions.Options;
using NWFM.Shared.Options;

namespace Platform.Application.Signup.Public;

/// <summary>One sign-in card on the landing page. Carries nothing beyond what the tenant's own login page shows.</summary>
public sealed record PublicTenantListItemDto(string Key, string Name, string Status, string LoginUrl);

/// <summary>
/// Workspaces the landing page offers a sign-in card for: only those that opted in and serve traffic, so a
/// suspended, provisioning or lapsed workspace is never advertised. Read from the catalog snapshot.
/// </summary>
public sealed record GetPublicTenantsQuery : IRequest<Result<IReadOnlyList<PublicTenantListItemDto>>>;

public sealed class GetPublicTenantsQueryHandler(ITenantStore tenantStore, IOptions<PlatformOptions> platformOptions)
    : IRequestHandler<GetPublicTenantsQuery, Result<IReadOnlyList<PublicTenantListItemDto>>>
{
    private const string LoginPath = "/login";

    public Task<Result<IReadOnlyList<PublicTenantListItemDto>>> Handle(GetPublicTenantsQuery request, CancellationToken cancellationToken)
    {
        var options = platformOptions.Value;

        IReadOnlyList<PublicTenantListItemDto> tenants = tenantStore.GetAll()
            .Where(t => t.IsServing && t.IsListedPublicly)
            .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .Select(t => new PublicTenantListItemDto(
                t.Key,
                t.Name,
                t.Status.ToString(),
                SignupStatusMapper.WorkspaceUrl(options, t.Key).TrimEnd('/') + LoginPath))
            .ToList();

        return Task.FromResult(Result<IReadOnlyList<PublicTenantListItemDto>>.Success(tenants));
    }
}
