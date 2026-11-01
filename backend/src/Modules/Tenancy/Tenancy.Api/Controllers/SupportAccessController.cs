using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NWFM.Shared.Constants;
using NWFM.Shared.Http;
using NWFM.Shared.MultiTenancy;
using NWFM.Shared.Results;
using Tenancy.Application.SupportAccess.Commands.EndCurrentSupportSession;
using Tenancy.Application.SupportAccess.Commands.ExchangeSupportHandoff;
using Tenancy.Application.SupportAccess.Commands.GrantSupportAccess;
using Tenancy.Application.SupportAccess.Commands.RevokeSupportAccess;
using Tenancy.Application.SupportAccess.Models;
using Tenancy.Application.SupportAccess.Queries.GetSupportAccess;

namespace Tenancy.Api.Controllers;

/// <summary>
/// The workspace's own consent for platform support to enter it: time-boxed, scoped and revocable.
/// </summary>
[ApiController]
[Authorize(Policy = NwfmPolicies.ManageSupportAccess)]
[Route("api/v1/support-access")]
public sealed class SupportAccessController(ISender sender) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(Result<SupportAccessHistoryDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetHistory(CancellationToken ct) =>
        (await sender.Send(new GetSupportAccessQuery(), ct)).ToActionResult();

    [HttpPost]
    [ProducesResponseType(typeof(Result<SupportAccessGrantDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Grant([FromBody] GrantSupportAccessCommand command, CancellationToken ct) =>
        (await sender.Send(command, ct)).ToActionResult();

    [HttpPost("{grantId:guid}/revoke")]
    [ProducesResponseType(typeof(Result<bool>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Revoke(Guid grantId, CancellationToken ct) =>
        (await sender.Send(new RevokeSupportAccessCommand { GrantId = grantId }, ct)).ToActionResult();
}

/// <summary>
/// The two ends of a support session inside the workspace. The handoff sits under auth: it is how a
/// support user signs in, and it shares the stricter sign-in rate limit.
/// </summary>
[ApiController]
[Route("api/v1/auth/support")]
public sealed class SupportSessionController(ISender sender) : ControllerBase
{
    [HttpPost("exchange")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(Result<SupportSessionTokenDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Exchange([FromBody] ExchangeSupportHandoffCommand command, CancellationToken ct) =>
        (await sender.Send(command, ct)).ToActionResult();

    [HttpPost("end")]
    [Authorize]
    [ProducesResponseType(typeof(Result<SupportSessionEndedDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> End(CancellationToken ct) =>
        (await sender.Send(new EndCurrentSupportSessionCommand(), ct)).ToActionResult();
}
