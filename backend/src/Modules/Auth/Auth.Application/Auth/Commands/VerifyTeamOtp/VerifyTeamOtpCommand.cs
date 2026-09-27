using Auth.Application.Auth.Models;
using MediatR;
using NWFM.Shared.Results;

namespace Auth.Application.Auth.Commands.VerifyTeamOtp;

public sealed record VerifyTeamOtpCommand(string ChallengeId, string Otp) : IRequest<Result<AuthTokenDto>>;
