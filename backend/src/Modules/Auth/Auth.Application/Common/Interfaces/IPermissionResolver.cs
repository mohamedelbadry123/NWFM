namespace Auth.Application.Common.Interfaces;

public interface IPermissionResolver
{
    Task<IReadOnlyList<string>> GetPermissionCodesForUserAsync(string userId, CancellationToken ct = default);
    Task<IReadOnlyList<string>> GetPermissionCodesForRoleAsync(string roleId, CancellationToken ct = default);
    Task InvalidateUserAsync(string userId, CancellationToken ct = default);
    Task InvalidateRoleAsync(string roleId, CancellationToken ct = default);
}
