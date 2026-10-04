namespace Auth.Application.Users.Models;

/// <summary>
/// One coverage row: a territory (or everywhere), narrowed to some departments and activity types.
/// An empty list covers all of them.
/// </summary>
public sealed class OrgScopeAssignmentDto
{
    public string? Level { get; init; }
    public string? Code { get; init; }
    public IReadOnlyList<string> DepartmentCodes { get; init; } = [];
    public IReadOnlyList<string> ActivityTypeCodes { get; init; } = [];
}
