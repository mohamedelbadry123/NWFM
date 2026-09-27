using Auth.Application.Auth.Models;
using Auth.Application.Common.Interfaces;
using Auth.Application.Constants;
using Auth.Domain.Options;
using MediatR;
using Microsoft.Extensions.Options;
using NWFM.Shared.Caching;
using NWFM.Shared.Constants;
using NWFM.Shared.Results;

namespace Auth.Application.Auth.Commands.ResendTeamOtp;

internal sealed class ResendTeamOtpCommandHandler(
    ISmsSender smsSender,
    ICacheService cacheService,
    IOptions<TeamOtpOptions> otpOptions)
    : IRequestHandler<ResendTeamOtpCommand, Result<TeamOtpChallengeDto>>
{
    public async Task<Result<TeamOtpChallengeDto>> Handle(ResendTeamOtpCommand request, CancellationToken cancellationToken)
    {
        string cacheKey = CacheKeys.Auth.Otp.ForChallenge(request.ChallengeId);

        TeamOtpChallenge? challenge = await cacheService.GetOrCreateAsync<TeamOtpChallenge?>(
            cacheKey,
            _ => new ValueTask<TeamOtpChallenge?>((TeamOtpChallenge?)null),
            CacheEntryOptions.FromMinutes(1, localMinutes: 1),
            cancellationToken);

        if (challenge is null)
            return Result<TeamOtpChallengeDto>.Failure(AuthErrors.OtpChallengeNotFound);

        long nowUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (nowUnix > challenge.ExpiresAtUnixSeconds)
        {
            await cacheService.RemoveAsync(cacheKey, cancellationToken);
            return Result<TeamOtpChallengeDto>.Failure(AuthErrors.OtpExpired);
        }

        if (nowUnix < challenge.ResendAllowedAtUnixSeconds)
            return Result<TeamOtpChallengeDto>.Failure(AuthErrors.OtpResendCooldown);

        TeamOtpOptions options = otpOptions.Value;
        string newOtp = TeamOtpCode.Generate(options.CodeLength);

        challenge.ResendAllowedAtUnixSeconds = nowUnix + options.ResendCooldownSeconds;

        // OtpHash is init-only; build a replacement challenge with the new hash
        var updatedChallenge = new TeamOtpChallenge
        {
            UserId = challenge.UserId,
            UserName = challenge.UserName,
            TeamId = challenge.TeamId,
            Mobile = challenge.Mobile,
            OtpHash = TeamOtpCode.Hash(newOtp),
            Attempts = challenge.Attempts,
            ExpiresAtUnixSeconds = challenge.ExpiresAtUnixSeconds,
            ResendAllowedAtUnixSeconds = challenge.ResendAllowedAtUnixSeconds,
            DeviceName = challenge.DeviceName,
            DeviceUuid = challenge.DeviceUuid,
            AppVersion = challenge.AppVersion,
            DeviceOs = challenge.DeviceOs,
            Latitude = challenge.Latitude,
            Longitude = challenge.Longitude
        };
        challenge = updatedChallenge;

        int remainingSeconds = (int)(challenge.ExpiresAtUnixSeconds - nowUnix);
        int cacheMinutes = Math.Max((int)Math.Ceiling(remainingSeconds / 60.0), 1);

        await cacheService.SetAsync(
            cacheKey,
            challenge,
            CacheEntryOptions.FromMinutes(cacheMinutes, localMinutes: cacheMinutes),
            cancellationToken);

        if (!string.IsNullOrWhiteSpace(challenge.Mobile))
        {
            string message = string.Format(options.MessageTemplateEn, newOtp);
            Result<bool> smsResult = await smsSender.SendAsync(challenge.Mobile, message, cancellationToken);

            if (!smsResult.IsSuccess)
                return Result<TeamOtpChallengeDto>.Failure(AuthErrors.SmsFailed);
        }

        return Result<TeamOtpChallengeDto>.Success(new TeamOtpChallengeDto
        {
            RequiresOtp = true,
            ChallengeId = request.ChallengeId,
            ExpiresInSeconds = remainingSeconds,
            MaskedMobile = TeamOtpCode.MaskMobile(challenge.Mobile)
        });
    }
}
