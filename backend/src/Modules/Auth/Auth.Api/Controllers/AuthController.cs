using Auth.Application.Auth.Commands.ExchangeSsoCode;
using Auth.Application.Auth.Commands.LoginTeam;
using Auth.Application.Auth.Commands.LoginUser;
using Auth.Application.Auth.Commands.Logout;
using Auth.Application.Auth.Commands.RefreshAccessToken;
using Auth.Application.Auth.Commands.ResendTeamOtp;
using Auth.Application.Auth.Commands.VerifyTeamOtp;
using Auth.Application.Auth.Models;
using Auth.Application.Auth.Queries.GetCurrentUserProfile;
using Auth.Application.Auth.Queries.GetSsoStatus;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using NWFM.Shared.Results;

namespace Auth.Api.Controllers;

[ApiController]
[Route("api/v1/auth")]
public sealed class AuthController(ISender sender) : ControllerBase
{
    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(Result<AuthTokenDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Login([FromBody] LoginUserCommand command, CancellationToken ct)
    {
        var result = await sender.Send(command, ct);
        return result.IsSuccess ? Ok(result) : Unauthorized(result);
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(Result<AuthTokenDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Refresh([FromBody] RefreshAccessTokenCommand command, CancellationToken ct)
    {
        var result = await sender.Send(command, ct);
        return result.IsSuccess ? Ok(result) : Unauthorized(result);
    }

    [HttpPost("logout")]
    [Authorize]
    [ProducesResponseType(typeof(Result<bool>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Logout(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] LogoutCommand? command,
        CancellationToken ct)
    {
        var result = await sender.Send(command ?? new LogoutCommand(), ct);
        return Ok(result);
    }

    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType(typeof(Result<CurrentUserProfileDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMyProfile(CancellationToken ct)
    {
        var result = await sender.Send(new GetCurrentUserProfileQuery(), ct);
        return result.IsSuccess ? Ok(result) : NotFound(result);
    }

    [HttpGet("sso/status")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(Result<SsoStatusDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> SsoStatus(CancellationToken ct)
    {
        var result = await sender.Send(new GetSsoStatusQuery(), ct);
        return Ok(result);
    }

    [HttpPost("sso/exchange")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(Result<AuthTokenDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ExchangeSsoCode([FromBody] ExchangeSsoCodeCommand command, CancellationToken ct)
    {
        var result = await sender.Send(command, ct);
        return result.IsSuccess ? Ok(result) : Unauthorized(result);
    }

    [HttpPost("team/login")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(Result<TeamOtpChallengeDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> TeamLogin([FromBody] LoginTeamCommand command, CancellationToken ct)
    {
        var result = await sender.Send(command, ct);
        return result.IsSuccess ? Ok(result) : Unauthorized(result);
    }

    [HttpPost("team/verify-otp")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(Result<AuthTokenDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> VerifyTeamOtp([FromBody] VerifyTeamOtpCommand command, CancellationToken ct)
    {
        var result = await sender.Send(command, ct);
        return result.IsSuccess ? Ok(result) : Unauthorized(result);
    }

    [HttpPost("team/resend-otp")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(Result<TeamOtpChallengeDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ResendTeamOtp([FromBody] ResendTeamOtpCommand command, CancellationToken ct)
    {
        var result = await sender.Send(command, ct);
        return result.IsSuccess ? Ok(result) : BadRequest(result);
    }
}
