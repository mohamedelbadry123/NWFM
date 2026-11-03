using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using NWFM.Shared.Http;
using NWFM.Shared.Results;
using Platform.Application.Common.Interfaces;
using Platform.Application.Signup.Public;

namespace Platform.Api.Controllers;

/// <summary>
/// Self-service subscription requests from the landing page. Anonymous, platform scoped (no tenant
/// is ever resolved here) and rate limited per caller IP. Status, verification and database calls
/// require the signup's e-mailed token.
/// </summary>
[ApiController]
[AllowAnonymous]
[EnableRateLimiting(PlatformRateLimits.Public)]
[Route("api/v1/public/signup")]
public sealed class PublicSignupController(ISender sender) : ControllerBase
{
    [HttpGet("options")]
    [ProducesResponseType(typeof(Result<SignupOptionsDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetOptions(CancellationToken ct) =>
        (await sender.Send(new GetSignupOptionsQuery(), ct)).ToActionResult();

    [HttpGet("key-availability")]
    [ProducesResponseType(typeof(Result<TenantKeyAvailabilityDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> CheckKey([FromQuery] string key, CancellationToken ct) =>
        (await sender.Send(new CheckTenantKeyQuery(key), ct)).ToActionResult();

    [HttpPost]
    [ProducesResponseType(typeof(Result<SubmitSignupDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Submit([FromBody] SubmitSignupCommand command, CancellationToken ct) =>
        (await sender.Send(command, ct)).ToActionResult();

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(Result<SignupStatusDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetStatus(Guid id, CancellationToken ct) =>
        (await sender.Send(new GetSignupStatusQuery(id), ct)).ToActionResult();

    [HttpPost("{id:guid}/verify")]
    [ProducesResponseType(typeof(Result<SignupStatusDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Verify(Guid id, [FromBody] VerifySignupEmailCommand command, CancellationToken ct) =>
        (await sender.Send(command with { SignupId = id }, ct)).ToActionResult();

    [HttpPost("{id:guid}/resend")]
    [ProducesResponseType(typeof(Result<bool>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Resend(Guid id, CancellationToken ct) =>
        (await sender.Send(new ResendSignupVerificationCommand(id), ct)).ToActionResult();

    [HttpGet("database/script")]
    [ProducesResponseType(typeof(Result<ByoSetupScriptDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetDatabaseScript(
        [FromQuery] string database, [FromQuery] string userName, [FromQuery] string runtimeUserName, CancellationToken ct) =>
        (await sender.Send(new GetByoSetupScriptQuery(database, userName, runtimeUserName), ct)).ToActionResult();

    [HttpPost("{id:guid}/database/test")]
    [EnableRateLimiting(PlatformRateLimits.DatabaseTest)]
    [ProducesResponseType(typeof(Result<ByoDatabaseInspection>), StatusCodes.Status200OK)]
    public async Task<IActionResult> TestDatabase(Guid id, [FromBody] TestSignupDatabaseCommand command, CancellationToken ct) =>
        (await sender.Send(command with { SignupId = id }, ct)).ToActionResult();

    [HttpPost("{id:guid}/database")]
    [EnableRateLimiting(PlatformRateLimits.DatabaseTest)]
    [ProducesResponseType(typeof(Result<SaveSignupDatabaseDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> SaveDatabase(Guid id, [FromBody] SaveSignupDatabaseCommand command, CancellationToken ct) =>
        (await sender.Send(command with { SignupId = id }, ct)).ToActionResult();
}

/// <summary>The landing page's list of workspaces that chose to be listed, with their sign-in addresses.</summary>
[ApiController]
[AllowAnonymous]
[EnableRateLimiting(PlatformRateLimits.Public)]
[Route("api/v1/public")]
public sealed class PublicCatalogController(ISender sender) : ControllerBase
{
    [HttpGet("workspaces")]
    [ProducesResponseType(typeof(Result<IReadOnlyList<PublicTenantListItemDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetWorkspaces(CancellationToken ct) =>
        (await sender.Send(new GetPublicTenantsQuery(), ct)).ToActionResult();
}
