using Auth.Application.Lookups.Queries;
using MediatR;
using NWFM.Shared.Results;

namespace Auth.Application.Lookups.Commands.UpdateLookup;

public sealed record UpdateLookupCommand : IRequest<Result<LookupItemDto>>
{
    public string LookupType { get; init; } = default!;
    public Guid Id { get; init; }
    public string NameEn { get; init; } = default!;
    public string NameAr { get; init; } = default!;
    public string? ParentCode { get; init; }
}
