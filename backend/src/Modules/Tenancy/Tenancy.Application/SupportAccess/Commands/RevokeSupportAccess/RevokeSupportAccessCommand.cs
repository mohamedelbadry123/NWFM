using NWFM.Shared.Constants;
using NWFM.Shared.Security;

namespace Tenancy.Application.SupportAccess.Commands.RevokeSupportAccess;

/// <summary>Withdraws a grant and ends every session still open under it.</summary>
[Authorize(Policy = NwfmPolicies.ManageSupportAccess)]
public sealed record RevokeSupportAccessCommand : IRequest<Result<bool>>
{
    public Guid GrantId { get; init; }
}
