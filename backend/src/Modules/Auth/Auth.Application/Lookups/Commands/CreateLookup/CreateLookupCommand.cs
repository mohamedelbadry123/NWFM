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

    /// <summary>Sources only: Internal or External.</summary>
    public string? Kind { get; init; }

    /// <summary>Sources only: where the source is reached; required when External.</summary>
    public string? Url { get; init; }

    /// <summary>Activity types only: the sources allowed to create one — at least one.</summary>
    public IReadOnlyList<string>? SourceCodes { get; init; }
}
