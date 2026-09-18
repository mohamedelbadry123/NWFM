namespace Auth.Api.Contracts;

public sealed class LookupListQuery
{
    public int PageNumber { get; init; } = 1;
    public int PageSize { get; init; } = 10;
    public string? SearchTerm { get; init; }
    public string? ParentCode { get; init; }
    public bool? IsActive { get; init; }
}

public sealed class CreateLookupRequest
{
    public string Code { get; init; } = default!;
    public string NameEn { get; init; } = default!;
    public string NameAr { get; init; } = default!;
    public string? ParentCode { get; init; }
}

public sealed class UpdateLookupRequest
{
    public string NameEn { get; init; } = default!;
    public string NameAr { get; init; } = default!;
    public string? ParentCode { get; init; }
}

public sealed class SetLookupStatusRequest
{
    public bool IsActive { get; init; }
}
