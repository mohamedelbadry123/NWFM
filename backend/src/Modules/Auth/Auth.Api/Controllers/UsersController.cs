using Auth.Application.Users.Commands.CreateUser;
using Auth.Application.Users.Commands.ResetUserPassword;
using Auth.Application.Users.Commands.SetUserStatus;
using Auth.Application.Users.Commands.UpdateUser;
using Auth.Application.Users.Models;
using Auth.Application.Users.Queries.GetAssignableRoles;
using Auth.Application.Users.Queries.GetUserById;
using Auth.Application.Users.Queries.GetUsers;
using NWFM.Shared.Constants;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NWFM.Shared.Results;

namespace Auth.Api.Controllers;

[ApiController]
[Authorize(Policy = NwfmPolicies.ManageUsers)]
[Route("api/v1/users")]
public sealed class UsersController(ISender sender) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(Result<PaginatedResult<UserListItemDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetUsers([FromQuery] GetUsersQuery query, CancellationToken ct)
    {
        var result = await sender.Send(query, ct);
        return Ok(result);
    }

    [HttpGet("roles")]
    [ProducesResponseType(typeof(Result<IReadOnlyList<string>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAssignableRoles(CancellationToken ct)
    {
        var result = await sender.Send(new GetAssignableRolesQuery(), ct);
        return Ok(result);
    }

    [HttpGet("{id}")]
    [ProducesResponseType(typeof(Result<UserDetailDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetUserById(string id, CancellationToken ct)
    {
        var result = await sender.Send(new GetUserByIdQuery { UserId = id }, ct);
        return result.IsSuccess ? Ok(result) : NotFound(result);
    }

    [HttpPost]
    [ProducesResponseType(typeof(Result<UserDetailDto>), StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateUser([FromBody] CreateUserCommand command, CancellationToken ct)
    {
        var result = await sender.Send(command, ct);
        return result.IsSuccess ? StatusCode(StatusCodes.Status201Created, result) : BadRequest(result);
    }

    [HttpPut("{id}")]
    [ProducesResponseType(typeof(Result<UserDetailDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateUser(string id, [FromBody] UpdateUserCommand command, CancellationToken ct)
    {
        var result = await sender.Send(command with { UserId = id }, ct);
        return result.IsSuccess ? Ok(result) : BadRequest(result);
    }

    [HttpPut("{id}/status")]
    [ProducesResponseType(typeof(Result<string>), StatusCodes.Status200OK)]
    public async Task<IActionResult> SetStatus(string id, [FromBody] SetUserStatusCommand command, CancellationToken ct)
    {
        var result = await sender.Send(command with { UserId = id }, ct);
        return result.IsSuccess ? Ok(result) : BadRequest(result);
    }

    [HttpPost("{id}/reset-password")]
    [ProducesResponseType(typeof(Result<string>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ResetPassword(string id, [FromBody] ResetUserPasswordCommand command, CancellationToken ct)
    {
        var result = await sender.Send(command with { UserId = id }, ct);
        return result.IsSuccess ? Ok(result) : BadRequest(result);
    }
}
