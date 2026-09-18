namespace Auth.Application.Permissions.Models;

public sealed class PermissionDto
{
    public Guid Id { get; init; }
    public string Code { get; init; } = default!;
    public string Module { get; init; } = default!;
    public string NameEn { get; init; } = default!;
    public string NameAr { get; init; } = default!;
    public bool IsActive { get; init; }
}

public sealed class RoleDto
{
    public string Id { get; init; } = default!;
    public string Name { get; init; } = default!;
    public IReadOnlyList<string> PermissionCodes { get; init; } = [];
}

public sealed class RolePermissionsDto
{
    public string RoleId { get; init; } = default!;
    public string RoleName { get; init; } = default!;
    public IReadOnlyList<PermissionDto> Permissions { get; init; } = [];
}
