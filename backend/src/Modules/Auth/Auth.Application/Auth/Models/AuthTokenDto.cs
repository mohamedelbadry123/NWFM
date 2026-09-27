namespace Auth.Application.Auth.Models;

public sealed class AuthTokenDto
{
    public string AccessToken { get; init; } = default!;
    public string RefreshToken { get; init; } = default!;
    public int ExpiresInSeconds { get; init; }
    public string UserName { get; init; } = default!;
    public IReadOnlyList<string> Roles { get; init; } = [];
    public IReadOnlyList<string> Permissions { get; init; } = [];
}
