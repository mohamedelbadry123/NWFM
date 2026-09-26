using Auth.Application.Common.Interfaces;
using Auth.Application.Permissions.Models;
using MediatR;
using Microsoft.EntityFrameworkCore;
using NWFM.Shared.Results;

namespace Auth.Application.Permissions.Queries.GetRolePermissions;

public sealed class GetRolePermissionsQueryHandler(IRoleLookup roleLookup, IAuthDbContext context)
    : IRequestHandler<GetRolePermissionsQuery, Result<RolePermissionsDto>>
{
    private static readonly Error RoleNotFound = new("Auth.RoleNotFound", "Role not found.");

    public async Task<Result<RolePermissionsDto>> Handle(GetRolePermissionsQuery request, CancellationToken ct)
    {
        var roleId = await roleLookup.GetRoleIdByNameAsync(request.RoleName, ct);
        if (roleId is null)
            return Result<RolePermissionsDto>.Failure(RoleNotFound);

        var permissions = await context.RolePermissions.AsNoTracking()
            .Where(rp => rp.RoleId == roleId)
            .Where(rp => rp.Permission.IsActive)
            .Select(rp => new PermissionDto
            {
                Id = rp.Permission.Id, Code = rp.Permission.Code, Module = rp.Permission.Module,
                NameEn = rp.Permission.NameEn, NameAr = rp.Permission.NameAr, IsActive = rp.Permission.IsActive
            })
            .ToListAsync(ct);

        return Result<RolePermissionsDto>.Success(new RolePermissionsDto
        {
            RoleId = roleId,
            RoleName = request.RoleName,
            Permissions = permissions
        });
    }
}
