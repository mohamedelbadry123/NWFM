using Auth.Application.Auth.Models;
using Auth.Application.Common.Interfaces;
using Auth.Application.Constants;
using Auth.Domain.Options;
using MediatR;
using Microsoft.Extensions.Options;
using NWFM.Shared.Caching;
using NWFM.Shared.Constants;
using NWFM.Shared.Results;

namespace Auth.Application.Auth.Commands.VerifyTeamOtp;

internal sealed class VerifyTeamOtpCommandHandler(
    IAuthService authService,
    ICacheService cacheService,
    IOptions<TeamOtpOptions> otpOptions)
    : IRequestHandler<VerifyTeamOtpCommand, Result<AuthTokenDto>>
{
    public async Task<Result<AuthTokenDto>> Handle(VerifyTeamOtpCommand request, CancellationToken cancellationToken)
    {
        string cacheKey = CacheKeys.Auth.Otp.ForChallenge(request.ChallengeId);

        TeamOtpChallenge? challenge = await cacheService.GetOrCreateAsync<TeamOtpChallenge?>(
            cacheKey,
            _ => new ValueTask<TeamOtpChallenge?>((TeamOtpChallenge?)null),
            CacheEntryOptions.FromMinutes(1, localMinutes: 1),
            cancellationToken);

        if (challenge is null)
            return Result<AuthTokenDto>.Failure(AuthErrors.OtpChallengeNotFound);

        long nowUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (nowUnix > challenge.ExpiresAtUnixSeconds)
        {
            await cacheService.RemoveAsync(cacheKey, cancellationToken);
            return Result<AuthTokenDto>.Failure(AuthErrors.OtpExpired);
        }

        TeamOtpOptions options = otpOptions.Value;
        if (challenge.Attempts >= options.MaxAttempts)
        {
            await cacheService.RemoveAsync(cacheKey, cancellationToken);
            return Result<AuthTokenDto>.Failure(AuthErrors.OtpMaxAttempts);
        }

        if (!TeamOtpCode.Verify(request.Otp, challenge.OtpHash))
        {
            challenge.Attempts++;
            await cacheService.SetAsync(
                cacheKey,
                challenge,
                CacheEntryOptions.FromMinutes(options.ExpiryMinutes, localMinutes: options.ExpiryMinutes),
                cancellationToken);
            return Result<AuthTokenDto>.Failure(AuthErrors.OtpInvalid);
        }

        await cacheService.RemoveAsync(cacheKey, cancellationToken);

        return await authService.IssueTeamTokensAsync(challenge.UserId, cancellationToken);
    }
}
