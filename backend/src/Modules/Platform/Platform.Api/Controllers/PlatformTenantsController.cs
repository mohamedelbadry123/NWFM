using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NWFM.Shared.Http;
using NWFM.Shared.Results;
using Platform.Api.Contracts;
using Platform.Application.Common.Interfaces;
using Platform.Application.Subscriptions;
using Platform.Application.Tenants.Admins;
using Platform.Application.Tenants.Commands;
using Platform.Application.Tenants.Connection;
using Platform.Application.Tenants.Models;
using Platform.Application.Tenants.Queries;
using Tenancy.Domain.Constants;

namespace Platform.Api.Controllers;

/// <summary>
/// Workspaces in the catalog: profile, status, subscription, schema migrations, database connection
/// and their administrators. Reads need platform staff; every change needs a platform administrator.
/// </summary>
[ApiController]
[Authorize(Policy = PlatformPolicies.Staff)]
[Route("api/v1/platform/tenants")]
public sealed class PlatformTenantsController(ISender sender) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(Result<PaginatedResult<PlatformTenantListItemDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTenants([FromQuery] TenantListQuery query, CancellationToken ct) =>
        (await sender.Send(new GetPlatformTenantsQuery
        {
            PageNumber = query.PageNumber,
            PageSize = query.PageSize,
            SearchTerm = query.SearchTerm,
            Status = query.Status,
            SortField = query.SortField,
            SortDescending = query.SortDescending
        }, ct)).ToActionResult();

    [HttpPost]
    [Authorize(Policy = PlatformPolicies.Admin)]
    [ProducesResponseType(typeof(Result<CreatePlatformTenantDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Create([FromBody] CreatePlatformTenantCommand command, CancellationToken ct) =>
        (await sender.Send(command, ct)).ToActionResult();

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(Result<PlatformTenantDetailDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) =>
        (await sender.Send(new GetPlatformTenantByIdQuery(id), ct)).ToActionResult();

    [HttpPut("{id:guid}")]
    [Authorize(Policy = PlatformPolicies.Admin)]
    [ProducesResponseType(typeof(Result<PlatformTenantDetailDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdatePlatformTenantCommand command, CancellationToken ct) =>
        (await sender.Send(command with { TenantId = id }, ct)).ToActionResult();

    [HttpPost("{id:guid}/status")]
    [Authorize(Policy = PlatformPolicies.Admin)]
    [ProducesResponseType(typeof(Result<PlatformTenantDetailDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ChangeStatus(Guid id, [FromBody] ChangePlatformTenantStatusCommand command, CancellationToken ct) =>
        (await sender.Send(command with { TenantId = id }, ct)).ToActionResult();

    [HttpPut("{id:guid}/subscription")]
    [Authorize(Policy = PlatformPolicies.Admin)]
    [ProducesResponseType(typeof(Result<PlatformTenantDetailDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateSubscription(Guid id, [FromBody] UpdatePlatformSubscriptionCommand command, CancellationToken ct) =>
        (await sender.Send(command with { TenantId = id }, ct)).ToActionResult();

    [HttpGet("{id:guid}/migrations/pending")]
    [ProducesResponseType(typeof(Result<IReadOnlyList<string>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPendingMigrations(Guid id, CancellationToken ct) =>
        (await sender.Send(new GetPlatformTenantPendingMigrationsQuery(id), ct)).ToActionResult();

    [HttpPost("{id:guid}/migrate")]
    [Authorize(Policy = PlatformPolicies.Admin)]
    [ProducesResponseType(typeof(Result<TenantMigrationOutcome>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Migrate(Guid id, CancellationToken ct) =>
        (await sender.Send(new MigratePlatformTenantCommand(id), ct)).ToActionResult();

    [HttpPost("migrate-all")]
    [Authorize(Policy = PlatformPolicies.Admin)]
    [ProducesResponseType(typeof(Result<bool>), StatusCodes.Status200OK)]
    public async Task<IActionResult> MigrateAll(CancellationToken ct) =>
        (await sender.Send(new MigrateAllPlatformTenantsCommand(), ct)).ToActionResult();

    [HttpPost("{id:guid}/database/verify")]
    [Authorize(Policy = PlatformPolicies.Admin)]
    [ProducesResponseType(typeof(Result<bool>), StatusCodes.Status200OK)]
    public async Task<IActionResult> VerifyDatabase(Guid id, CancellationToken ct) =>
        (await sender.Send(new VerifyPlatformTenantDatabaseCommand(id), ct)).ToActionResult();

    [HttpGet("{id:guid}/database/connection")]
    [Authorize(Policy = PlatformPolicies.Admin)]
    [ProducesResponseType(typeof(Result<TenantConnectionDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetConnection(Guid id, CancellationToken ct) =>
        (await sender.Send(new GetPlatformTenantConnectionQuery(id), ct)).ToActionResult();

    [HttpPost("{id:guid}/database/connection/reveal")]
    [Authorize(Policy = PlatformPolicies.Admin)]
    [ProducesResponseType(typeof(Result<TenantConnectionDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> RevealConnection(Guid id, [FromBody] RevealPlatformTenantConnectionCommand command, CancellationToken ct)
    {
        // A revealed connection string must never sit in a browser or proxy cache.
        Response.Headers.CacheControl = "no-store";
        return (await sender.Send(command with { TenantId = id }, ct)).ToActionResult();
    }

    [HttpGet("{id:guid}/admins")]
    [ProducesResponseType(typeof(Result<IReadOnlyList<TenantAdministratorDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAdmins(Guid id, CancellationToken ct) =>
        (await sender.Send(new GetPlatformTenantAdminsQuery(id), ct)).ToActionResult();

    [HttpPost("{id:guid}/admins")]
    [Authorize(Policy = PlatformPolicies.Admin)]
    [ProducesResponseType(typeof(Result<PlatformTenantAdminCredentialsDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> CreateAdmin(Guid id, [FromBody] CreatePlatformTenantAdminCommand command, CancellationToken ct) =>
        (await sender.Send(command with { TenantId = id }, ct)).ToActionResult();

    [HttpPost("{id:guid}/admins/{userId}/status")]
    [Authorize(Policy = PlatformPolicies.Admin)]
    [ProducesResponseType(typeof(Result<TenantAdministratorDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ChangeAdminStatus(Guid id, string userId,
        [FromBody] ChangePlatformTenantAdminStatusCommand command, CancellationToken ct) =>
        (await sender.Send(command with { TenantId = id, UserId = userId }, ct)).ToActionResult();

    [HttpPost("{id:guid}/admins/{userId}/resend-credentials")]
    [Authorize(Policy = PlatformPolicies.Admin)]
    [ProducesResponseType(typeof(Result<PlatformTenantAdminCredentialsDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ResendAdminCredentials(Guid id, string userId, CancellationToken ct) =>
        (await sender.Send(new ResendPlatformTenantAdminCredentialsCommand(id, userId), ct)).ToActionResult();
}
