namespace Auth.Application.Users.Models;

public sealed class OrgScopeAssignmentDto
{
    public string? Level { get; init; }
    public string? Code { get; init; }
    public string? DepartmentId { get; init; }
}
