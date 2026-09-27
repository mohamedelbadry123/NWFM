namespace Auth.Application.Auth.Models;

public sealed class TeamOtpChallenge
{
    public string UserId { get; init; } = default!;
    public string UserName { get; init; } = default!;
    public Guid TeamId { get; init; }
    public string? Mobile { get; init; }
    public string OtpHash { get; init; } = default!;
    public int Attempts { get; set; }
    public long ExpiresAtUnixSeconds { get; init; }
    public long ResendAllowedAtUnixSeconds { get; set; }
    public string? DeviceName { get; init; }
    public string? DeviceUuid { get; init; }
    public string? AppVersion { get; init; }
    public string? DeviceOs { get; init; }
    public double? Latitude { get; init; }
    public double? Longitude { get; init; }
}
