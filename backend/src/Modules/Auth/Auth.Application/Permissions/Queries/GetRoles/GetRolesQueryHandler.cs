using Auth.Application.Common.Interfaces;
using Auth.Application.Permissions.Models;
using MediatR;
using Microsoft.EntityFrameworkCore;
using NWFM.Shared.Results;

namespace Auth.Application.Permissions.Queries.GetRoles;

public sealed class GetRolesQueryHandler(IRoleLookup roleLookup, IAuthDbContext context)
    : IRequestHandler<GetRolesQuery, Result<IReadOnlyList<RoleDto>>>
{
    public async Task<Result<IReadOnlyList<RoleDto>>> Handle(GetRolesQuery request, CancellationToken ct)
    {
        var roles = await roleLookup.GetRolesAsync(ct);

        var rolePermissions = await context.RolePermissions.AsNoTracking()
            .Where(rp => rp.Permission.IsActive)
            .Select(rp => new { rp.RoleId, rp.Permission.Code })
            .ToListAsync(ct);

        var permsByRole = rolePermissions
            .GroupBy(x => x.RoleId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Code).ToList());

        var result = roles.Select(r => new RoleDto
        {
            Id = r.Id,
            Name = r.Name,
            PermissionCodes = permsByRole.GetValueOrDefault(r.Id, [])
        }).ToList();

        return Result<IReadOnlyList<RoleDto>>.Success(result);
    }
}
