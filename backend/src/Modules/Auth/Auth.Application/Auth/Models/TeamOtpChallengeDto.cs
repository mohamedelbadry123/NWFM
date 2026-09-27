namespace Auth.Application.Auth.Models;

public sealed class TeamOtpChallengeDto
{
    public bool RequiresOtp { get; init; }
    public string ChallengeId { get; init; } = default!;
    public int? ExpiresInSeconds { get; init; }
    public string? MaskedMobile { get; init; }
}
