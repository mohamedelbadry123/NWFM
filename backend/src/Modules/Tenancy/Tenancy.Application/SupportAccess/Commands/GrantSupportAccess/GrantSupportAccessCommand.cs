using NWFM.Shared.Constants;
using NWFM.Shared.Security;
using Tenancy.Application.SupportAccess.Models;

namespace Tenancy.Application.SupportAccess.Commands.GrantSupportAccess;

/// <summary>A tenant administrator lets platform support into the workspace for a bounded time.</summary>
[Authorize(Policy = NwfmPolicies.ManageSupportAccess)]
public sealed record GrantSupportAccessCommand : IRequest<Result<SupportAccessGrantDto>>
{
    public string Scope { get; init; } = SupportScopes.ReadOnly;

    public int Hours { get; init; } = 8;

    public string Reason { get; init; } = default!;
}
