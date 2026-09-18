using Auth.Application.Lookups.Queries;
using MediatR;
using NWFM.Shared.Results;

namespace Auth.Application.Lookups.Commands.SetLookupStatus;

public sealed record SetLookupStatusCommand : IRequest<Result<LookupItemDto>>
{
    public string LookupType { get; init; } = default!;
    public Guid Id { get; init; }
    public bool IsActive { get; init; }
}
