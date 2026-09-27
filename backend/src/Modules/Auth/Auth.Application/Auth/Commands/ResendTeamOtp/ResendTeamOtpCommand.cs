using Auth.Application.Auth.Models;
using MediatR;
using NWFM.Shared.Results;

namespace Auth.Application.Auth.Commands.ResendTeamOtp;

public sealed record ResendTeamOtpCommand(string ChallengeId) : IRequest<Result<TeamOtpChallengeDto>>;
