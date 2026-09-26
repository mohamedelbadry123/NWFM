namespace Auth.Domain.Entities;

public sealed class RolePermission
{
    private RolePermission() { }

    private RolePermission(string roleId, Guid permissionId)
    {
        Id = Guid.NewGuid();
        RoleId = roleId;
        PermissionId = permissionId;
    }

    public Guid Id { get; private set; }
    public string RoleId { get; private set; } = default!;
    public Guid PermissionId { get; private set; }
    public Permission Permission { get; private set; } = default!;

    public static RolePermission Create(string roleId, Guid permissionId) =>
        new(roleId, permissionId);
}
