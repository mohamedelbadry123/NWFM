using Platform.Application.Common.Security;
using Platform.Application.Tenants.Common;
using Platform.Application.Tenants.Models;
using Tenancy.Application.Common.Interfaces;

namespace Platform.Application.Tenants.Queries;

[PlatformAuthorize]
public sealed record GetPlatformTenantByIdQuery(Guid TenantId) : IRequest<Result<PlatformTenantDetailDto>>;

public sealed class GetPlatformTenantByIdQueryHandler(ICatalogDbContext catalog)
    : IRequestHandler<GetPlatformTenantByIdQuery, Result<PlatformTenantDetailDto>>
{
    public async Task<Result<PlatformTenantDetailDto>> Handle(GetPlatformTenantByIdQuery request, CancellationToken cancellationToken)
    {
        var tenant = await PlatformTenantQueries.LoadAsync(catalog, request.TenantId, cancellationToken);

        return tenant is null
            ? PlatformResults.Fail<PlatformTenantDetailDto>("Workspace not found.", ApiErrorCodes.TenantNotFound, httpStatusCode: 404)
            : Result<PlatformTenantDetailDto>.Success(tenant.ToDetailDto());
    }
}
