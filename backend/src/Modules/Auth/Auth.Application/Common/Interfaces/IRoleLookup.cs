namespace Auth.Application.Common.Interfaces;

public sealed record RoleSummary(string Id, string Name);

public interface IRoleLookup
{
    Task<string?> GetRoleIdByNameAsync(string roleName, CancellationToken ct = default);
    Task<bool> RoleExistsAsync(string roleName, CancellationToken ct = default);
    Task<IReadOnlyList<RoleSummary>> GetRolesAsync(CancellationToken ct = default);
}
