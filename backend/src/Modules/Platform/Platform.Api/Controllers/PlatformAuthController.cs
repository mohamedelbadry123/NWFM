using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using NWFM.Shared.Http;
using NWFM.Shared.Results;
using Platform.Application.Auth;
using Tenancy.Domain.Constants;

namespace Platform.Api.Controllers;

/// <summary>Platform staff sign-in and profile. Only the platform scheme is accepted; a tenant token never is.</summary>
[ApiController]
[Authorize(Policy = PlatformPolicies.Staff)]
[Route("api/v1/platform/auth")]
public sealed class PlatformAuthController(ISender sender) : ControllerBase
{
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(PlatformRateLimits.Login)]
    [ProducesResponseType(typeof(Result<PlatformLoginDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Login([FromBody] PlatformLoginCommand command, CancellationToken ct) =>
        (await sender.Send(command, ct)).ToActionResult();

    [HttpGet("me")]
    [ProducesResponseType(typeof(Result<PlatformProfileDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Me(CancellationToken ct) =>
        (await sender.Send(new GetPlatformProfileQuery(), ct)).ToActionResult();
}
