using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NWFM.Shared.Http;
using NWFM.Shared.Results;
using Platform.Application.Common.Interfaces;
using Platform.Application.Support;
using Tenancy.Application.SupportAccess.Models;
using Tenancy.Domain.Constants;

namespace Platform.Api.Controllers;

/// <summary>
/// Support sessions under a workspace's own grant. The console never receives a tenant token: a
/// session yields a single-use link the tenant app redeems against the tenant API.
/// </summary>
[ApiController]
[Authorize(Policy = PlatformPolicies.Staff)]
[Route("api/v1/platform")]
public sealed class PlatformSupportController(ISender sender) : ControllerBase
{
    [HttpGet("tenants/{id:guid}/support/grants")]
    [ProducesResponseType(typeof(Result<IReadOnlyList<SupportAccessGrantDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetGrants(Guid id, CancellationToken ct) =>
        (await sender.Send(new GetPlatformSupportGrantsQuery { TenantId = id }, ct)).ToActionResult();

    [HttpGet("tenants/{id:guid}/support/sessions")]
    [ProducesResponseType(typeof(Result<IReadOnlyList<SupportAccessSessionDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSessions(Guid id, CancellationToken ct) =>
        (await sender.Send(new GetPlatformSupportSessionsQuery { TenantId = id }, ct)).ToActionResult();

    [HttpGet("tenants/{id:guid}/users")]
    [ProducesResponseType(typeof(Result<IReadOnlyList<SupportImpersonationCandidateDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTenantUsers(Guid id, [FromQuery] string? searchTerm, CancellationToken ct) =>
        (await sender.Send(new GetPlatformTenantUsersQuery { TenantId = id, SearchTerm = searchTerm }, ct)).ToActionResult();

    [HttpPost("support/sessions")]
    [ProducesResponseType(typeof(Result<SupportSessionHandoffDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Start([FromBody] StartSupportSessionCommand command, CancellationToken ct) =>
        (await sender.Send(command, ct)).ToActionResult();

    [HttpPost("support/sessions/{id:guid}/end")]
    [ProducesResponseType(typeof(Result<bool>), StatusCodes.Status200OK)]
    public async Task<IActionResult> End(Guid id, CancellationToken ct) =>
        (await sender.Send(new EndSupportSessionCommand { SessionId = id }, ct)).ToActionResult();
}
