using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NWFM.Shared.Http;
using NWFM.Shared.Results;
using Platform.Api.Contracts;
using Platform.Application.Audit;
using Platform.Application.DatabaseServers;
using Platform.Application.Plans;
using Platform.Application.Signup.Admin;
using Platform.Application.Users;
using Tenancy.Domain.Constants;

namespace Platform.Api.Controllers;

/// <summary>Plans offered on the landing page and the limits they carry.</summary>
[ApiController]
[Authorize(Policy = PlatformPolicies.Staff)]
[Route("api/v1/platform/plans")]
public sealed class PlatformPlansController(ISender sender) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(Result<IReadOnlyList<PlatformPlanDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(CancellationToken ct) =>
        (await sender.Send(new GetPlatformPlansQuery(), ct)).ToActionResult();

    [HttpPost]
    [Authorize(Policy = PlatformPolicies.Admin)]
    [ProducesResponseType(typeof(Result<int>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Create([FromBody] SavePlatformPlanCommand command, CancellationToken ct) =>
        (await sender.Send(command with { Id = null }, ct)).ToActionResult();

    [HttpPut("{id:int}")]
    [Authorize(Policy = PlatformPolicies.Admin)]
    [ProducesResponseType(typeof(Result<int>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(int id, [FromBody] SavePlatformPlanCommand command, CancellationToken ct) =>
        (await sender.Send(command with { Id = id }, ct)).ToActionResult();
}

/// <summary>The SQL Server pool managed tenant databases are created on. Admin only: it holds server credentials.</summary>
[ApiController]
[Authorize(Policy = PlatformPolicies.Admin)]
[Route("api/v1/platform/database-servers")]
public sealed class PlatformDatabaseServersController(ISender sender) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(Result<IReadOnlyList<PlatformDatabaseServerDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(CancellationToken ct) =>
        (await sender.Send(new GetPlatformDatabaseServersQuery(), ct)).ToActionResult();

    [HttpPost]
    [ProducesResponseType(typeof(Result<int>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Create([FromBody] SavePlatformDatabaseServerCommand command, CancellationToken ct) =>
        (await sender.Send(command with { Id = null }, ct)).ToActionResult();

    [HttpPut("{id:int}")]
    [ProducesResponseType(typeof(Result<int>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(int id, [FromBody] SavePlatformDatabaseServerCommand command, CancellationToken ct) =>
        (await sender.Send(command with { Id = id }, ct)).ToActionResult();
}

/// <summary>Platform staff accounts. Never tenant users: the two identity stores do not meet.</summary>
[ApiController]
[Authorize(Policy = PlatformPolicies.Admin)]
[Route("api/v1/platform/users")]
public sealed class PlatformUsersController(ISender sender) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(Result<IReadOnlyList<PlatformUserDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(CancellationToken ct) =>
        (await sender.Send(new GetPlatformUsersQuery(), ct)).ToActionResult();

    [HttpPost]
    [ProducesResponseType(typeof(Result<Guid>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Create([FromBody] SavePlatformUserCommand command, CancellationToken ct) =>
        (await sender.Send(command with { Id = null }, ct)).ToActionResult();

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(Result<Guid>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(Guid id, [FromBody] SavePlatformUserCommand command, CancellationToken ct) =>
        (await sender.Send(command with { Id = id }, ct)).ToActionResult();
}

/// <summary>Subscription requests: review, approve or reject, and retry a failed provisioning.</summary>
[ApiController]
[Authorize(Policy = PlatformPolicies.Staff)]
[Route("api/v1/platform/signups")]
public sealed class PlatformSignupsController(ISender sender) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(Result<PaginatedResult<PlatformSignupDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get([FromQuery] SignupListQuery query, CancellationToken ct) =>
        (await sender.Send(new GetPlatformSignupsQuery
        {
            PageNumber = query.PageNumber,
            PageSize = query.PageSize,
            Status = query.Status,
            SearchTerm = query.SearchTerm
        }, ct)).ToActionResult();

    [HttpPost("{id:guid}/approve")]
    [Authorize(Policy = PlatformPolicies.Admin)]
    [ProducesResponseType(typeof(Result<bool>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Approve(Guid id, CancellationToken ct) =>
        (await sender.Send(new ApprovePlatformSignupCommand(id), ct)).ToActionResult();

    [HttpPost("{id:guid}/reject")]
    [Authorize(Policy = PlatformPolicies.Admin)]
    [ProducesResponseType(typeof(Result<bool>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Reject(Guid id, [FromBody] RejectPlatformSignupCommand command, CancellationToken ct) =>
        (await sender.Send(command with { SignupId = id }, ct)).ToActionResult();

    [HttpPost("{id:guid}/retry")]
    [Authorize(Policy = PlatformPolicies.Admin)]
    [ProducesResponseType(typeof(Result<bool>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Retry(Guid id, CancellationToken ct) =>
        (await sender.Send(new RetryPlatformSignupCommand(id), ct)).ToActionResult();
}

/// <summary>The platform audit trail: every control-plane change and support action.</summary>
[ApiController]
[Authorize(Policy = PlatformPolicies.Staff)]
[Route("api/v1/platform/audit")]
public sealed class PlatformAuditController(ISender sender) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(Result<PaginatedResult<PlatformAuditEntryDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get([FromQuery] AuditListQuery query, CancellationToken ct) =>
        (await sender.Send(new GetPlatformAuditQuery
        {
            PageNumber = query.PageNumber,
            PageSize = query.PageSize,
            TenantId = query.TenantId,
            Action = query.Action,
            SearchTerm = query.SearchTerm,
            FromUtc = query.FromUtc,
            ToUtc = query.ToUtc
        }, ct)).ToActionResult();
}
