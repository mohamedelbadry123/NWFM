namespace Auth.Application.Auth.Models;

public sealed class TeamLoginContext
{
    public string UserId { get; init; } = default!;
    public string UserName { get; init; } = default!;
    public long TeamId { get; init; }
    public string? Mobile { get; init; }
}
