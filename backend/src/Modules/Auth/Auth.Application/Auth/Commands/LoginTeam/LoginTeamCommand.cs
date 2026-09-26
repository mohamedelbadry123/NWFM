using Auth.Application.Auth.Models;
using MediatR;
using NWFM.Shared.Results;

namespace Auth.Application.Auth.Commands.LoginTeam;

public sealed record LoginTeamCommand : IRequest<Result<TeamOtpChallengeDto>>
{
    public string UserCode { get; init; } = default!;
    public string Password { get; init; } = default!;
    public string? DeviceName { get; init; }
    public string? DeviceUuid { get; init; }
    public string? AppVersion { get; init; }
    public string? DeviceOs { get; init; }
    public double? Latitude { get; init; }
    public double? Longitude { get; init; }
}
