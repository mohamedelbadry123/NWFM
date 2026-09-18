using Auth.Application.Common.Interfaces;
using Auth.Application.Permissions.Models;
using Auth.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;
using NWFM.Shared.Results;

namespace Auth.Application.Permissions.Commands.AssignRolePermissions;

public sealed class AssignRolePermissionsCommandHandler(
    IRoleLookup roleLookup,
    IAuthDbContext context,
    IPermissionResolver permissionResolver)
    : IRequestHandler<AssignRolePermissionsCommand, Result<RolePermissionsDto>>
{
    private static readonly Error RoleNotFound = new("Auth.RoleNotFound", "Role not found.");

    public async Task<Result<RolePermissionsDto>> Handle(
        AssignRolePermissionsCommand request, CancellationToken ct)
    {
        var roleId = await roleLookup.GetRoleIdByNameAsync(request.RoleName, ct);
        if (roleId is null)
            return Result<RolePermissionsDto>.Failure(RoleNotFound);

        var validPermissions = await context.Permissions.AsNoTracking()
            .Where(p => p.IsActive && request.PermissionCodes.Contains(p.Code))
            .ToListAsync(ct);

        var existing = await context.RolePermissions
            .Where(rp => rp.RoleId == roleId)
            .ToListAsync(ct);

        context.RolePermissions.RemoveRange(existing);

        foreach (var permission in validPermissions)
            context.RolePermissions.Add(RolePermission.Create(roleId, permission.Id));

        await context.SaveChangesAsync(ct);
        await permissionResolver.InvalidateRoleAsync(roleId, ct);

        var result = validPermissions.Select(p => new PermissionDto
        {
            Id = p.Id, Code = p.Code, Module = p.Module,
            NameEn = p.NameEn, NameAr = p.NameAr, IsActive = p.IsActive
        }).ToList();

        return Result<RolePermissionsDto>.Success(new RolePermissionsDto
        {
            RoleId = roleId,
            RoleName = request.RoleName,
            Permissions = result
        });
    }
}
