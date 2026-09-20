using Auth.Application.Lookups.Queries;
using MediatR;
using NWFM.Shared.Constants;
using NWFM.Shared.Results;
using NWFM.Shared.Security;

namespace Auth.Application.Lookups.Commands.SetLookupStatus;

[Authorize(Policy = NwfmPolicies.ManageLookups)]
public sealed record SetLookupStatusCommand : IRequest<Result<LookupItemDto>>
{
    public string LookupType { get; init; } = default!;
    public Guid Id { get; init; }
    public bool IsActive { get; init; }
}
