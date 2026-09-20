using Auth.Application.Permissions.Commands.AssignRolePermissions;
using Auth.Application.Permissions.Models;
using Auth.Application.Permissions.Queries.GetPermissions;
using Auth.Application.Permissions.Queries.GetRolePermissions;
using Auth.Application.Permissions.Queries.GetRoles;
using NWFM.Shared.Constants;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NWFM.Shared.Results;

namespace Auth.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/admin")]
public sealed class AdminController(ISender sender) : ControllerBase
{
    [HttpGet("permissions")]
    [Authorize(Policy = NwfmPolicies.CanManageRolePermissions)]
    [ProducesResponseType(typeof(Result<IReadOnlyList<PermissionDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPermissions(CancellationToken ct)
    {
        var result = await sender.Send(new GetPermissionsQuery(), ct);
        return Ok(result);
    }

    [HttpGet("roles")]
    [Authorize(Policy = NwfmPolicies.CanManageRolePermissions)]
    [ProducesResponseType(typeof(Result<IReadOnlyList<RoleDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetRoles(CancellationToken ct)
    {
        var result = await sender.Send(new GetRolesQuery(), ct);
        return Ok(result);
    }

    [HttpGet("roles/{roleName}/permissions")]
    [Authorize(Policy = NwfmPolicies.CanManageRolePermissions)]
    [ProducesResponseType(typeof(Result<RolePermissionsDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetRolePermissions(string roleName, CancellationToken ct)
    {
        var result = await sender.Send(new GetRolePermissionsQuery { RoleName = roleName }, ct);
        return result.IsSuccess ? Ok(result) : NotFound(result);
    }

    [HttpPut("roles/{roleName}/permissions")]
    [Authorize(Policy = NwfmPolicies.CanManageRolePermissions)]
    [ProducesResponseType(typeof(Result<RolePermissionsDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> AssignRolePermissions(
        string roleName,
        [FromBody] AssignRolePermissionsCommand command,
        CancellationToken ct)
    {
        var result = await sender.Send(command with { RoleName = roleName }, ct);
        return result.IsSuccess ? Ok(result) : BadRequest(result);
    }
}
