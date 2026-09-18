using MediatR;
using NWFM.Shared.Results;

namespace Auth.Application.Lookups.Queries;

public sealed record GetLookupsQuery : IRequest<Result<PaginatedResult<LookupItemDto>>>
{
    public string LookupType { get; init; } = default!;
    public int PageNumber { get; init; } = 1;
    public int PageSize { get; init; } = 10;
    public string? SearchTerm { get; init; }
}

public sealed class LookupItemDto
{
    public Guid Id { get; init; }
    public string Code { get; init; } = default!;
    public string NameEn { get; init; } = default!;
    public string NameAr { get; init; } = default!;
    public bool IsActive { get; init; }
    public string? ParentCode { get; init; }
}
