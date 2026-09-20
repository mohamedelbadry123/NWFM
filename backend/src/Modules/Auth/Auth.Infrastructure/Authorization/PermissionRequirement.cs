using Microsoft.AspNetCore.Authorization;

namespace Auth.Infrastructure.Authorization;

public sealed class PermissionRequirement(string permissionCode) : IAuthorizationRequirement
{
    public string PermissionCode { get; } = permissionCode;
}

public sealed class AnyPermissionRequirement(IReadOnlyList<string> permissionCodes) : IAuthorizationRequirement
{
    public IReadOnlyList<string> PermissionCodes { get; } = permissionCodes;
}
