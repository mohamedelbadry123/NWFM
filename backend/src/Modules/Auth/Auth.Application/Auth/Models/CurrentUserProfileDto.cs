namespace Auth.Application.Auth.Models;

public sealed class CurrentUserProfileDto
{
    public string UserId { get; init; } = default!;
    public string UserName { get; init; } = default!;
    public string? Email { get; init; }
    public IReadOnlyList<string> Roles { get; init; } = [];
    public IReadOnlyList<string> Permissions { get; init; } = [];
    public long? TeamId { get; init; }
    public bool IsUnrestrictedScope { get; init; }
    public IReadOnlyList<AuthScopeDto> Scopes { get; init; } = [];
}

public sealed class AuthScopeDto
{
    public Guid ScopeId { get; init; }
    public string? Level { get; init; }
    public string? Code { get; init; }
    public string? DepartmentId { get; init; }
}
