namespace NWFM.Shared.Abstractions;

public interface ICurrentUser
{
    string? Id { get; }
    string? UserName { get; }
    string? Email { get; }
    IReadOnlyList<string> Roles { get; }
    IReadOnlyList<string> Permissions { get; }
    long? TeamId { get; }
    bool IsInRole(string role);
    bool HasPermission(string permissionCode);
}
