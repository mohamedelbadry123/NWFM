using Auth.Application.Lookups.Queries;
using MediatR;
using NWFM.Shared.Constants;
using NWFM.Shared.Results;
using NWFM.Shared.Security;

namespace Auth.Application.Lookups.Commands.CreateLookup;

[Authorize(Policy = NwfmPolicies.ManageLookups)]
public sealed record CreateLookupCommand : IRequest<Result<LookupItemDto>>
{
    public string LookupType { get; init; } = default!;
    public string Code { get; init; } = default!;
    public string NameEn { get; init; } = default!;
    public string NameAr { get; init; } = default!;
    public string? ParentCode { get; init; }
}
