using Auth.Application.Common.Interfaces;
using Auth.Application.Permissions.Models;
using MediatR;
using Microsoft.EntityFrameworkCore;
using NWFM.Shared.Results;

namespace Auth.Application.Permissions.Queries.GetPermissions;

public sealed class GetPermissionsQueryHandler(IAuthDbContext context)
    : IRequestHandler<GetPermissionsQuery, Result<IReadOnlyList<PermissionDto>>>
{
    public async Task<Result<IReadOnlyList<PermissionDto>>> Handle(
        GetPermissionsQuery request, CancellationToken ct)
    {
        var permissions = await context.Permissions.AsNoTracking()
            .OrderBy(p => p.Module).ThenBy(p => p.Code)
            .Select(p => new PermissionDto
            {
                Id = p.Id, Code = p.Code, Module = p.Module,
                NameEn = p.NameEn, NameAr = p.NameAr, IsActive = p.IsActive
            })
            .ToListAsync(ct);

        return Result<IReadOnlyList<PermissionDto>>.Success(permissions);
    }
}
