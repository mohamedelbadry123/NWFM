namespace Auth.Application.Users.Models;

public sealed class UserListItemDto
{
    public string Id { get; init; } = default!;
    public string UserName { get; init; } = default!;
    public string? Email { get; init; }
    public bool IsEnabled { get; init; }
    public IReadOnlyList<string> Roles { get; init; } = [];
}
