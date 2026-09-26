using Auth.Application.Auth.Models;
using Auth.Application.Common.Interfaces;
using Auth.Application.Constants;
using Auth.Domain.Options;
using MediatR;
using Microsoft.Extensions.Options;
using NWFM.Shared.Caching;
using NWFM.Shared.Constants;
using NWFM.Shared.Results;

namespace Auth.Application.Auth.Commands.LoginTeam;

internal sealed class LoginTeamCommandHandler(
    IAuthService authService,
    ISmsSender smsSender,
    ICacheService cacheService,
    IOptions<TeamOtpOptions> otpOptions)
    : IRequestHandler<LoginTeamCommand, Result<TeamOtpChallengeDto>>
{
    public async Task<Result<TeamOtpChallengeDto>> Handle(LoginTeamCommand request, CancellationToken cancellationToken)
    {
        Result<TeamLoginContext> authResult = await authService.AuthenticateTeamAsync(
            request.UserCode, request.Password, cancellationToken);

        if (!authResult.IsSuccess)
            return Result<TeamOtpChallengeDto>.Failure(authResult.Error);

        TeamLoginContext context = authResult.Value;
        TeamOtpOptions options = otpOptions.Value;

        string otp = TeamOtpCode.Generate(options.CodeLength);
        string otpHash = TeamOtpCode.Hash(otp);

        string challengeId = Guid.NewGuid().ToString("N");
        long nowUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var challenge = new TeamOtpChallenge
        {
            UserId = context.UserId,
            UserName = context.UserName,
            TeamId = context.TeamId,
            Mobile = context.Mobile,
            OtpHash = otpHash,
            Attempts = 0,
            ExpiresAtUnixSeconds = nowUnix + (options.ExpiryMinutes * 60),
            ResendAllowedAtUnixSeconds = nowUnix + options.ResendCooldownSeconds,
            DeviceName = request.DeviceName,
            DeviceUuid = request.DeviceUuid,
            AppVersion = request.AppVersion,
            DeviceOs = request.DeviceOs,
            Latitude = request.Latitude,
            Longitude = request.Longitude
        };

        await cacheService.SetAsync(
            CacheKeys.Auth.Otp.ForChallenge(challengeId),
            challenge,
            CacheEntryOptions.FromMinutes(options.ExpiryMinutes, localMinutes: options.ExpiryMinutes),
            cancellationToken);

        if (!string.IsNullOrWhiteSpace(context.Mobile))
        {
            string message = string.Format(options.MessageTemplateEn, otp);
            Result<bool> smsResult = await smsSender.SendAsync(context.Mobile, message, cancellationToken);

            if (!smsResult.IsSuccess)
                return Result<TeamOtpChallengeDto>.Failure(AuthErrors.SmsFailed);
        }

        return Result<TeamOtpChallengeDto>.Success(new TeamOtpChallengeDto
        {
            RequiresOtp = true,
            ChallengeId = challengeId,
            ExpiresInSeconds = options.ExpiryMinutes * 60,
            MaskedMobile = TeamOtpCode.MaskMobile(context.Mobile)
        });
    }
}
