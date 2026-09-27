using FluentValidation;

namespace Auth.Application.Auth.Commands.VerifyTeamOtp;

public sealed class VerifyTeamOtpCommandValidator : AbstractValidator<VerifyTeamOtpCommand>
{
    public VerifyTeamOtpCommandValidator()
    {
        RuleFor(x => x.ChallengeId)
            .NotEmpty()
            .WithMessage("Challenge ID is required.");

        RuleFor(x => x.Otp)
            .NotEmpty()
            .WithMessage("OTP code is required.");
    }
}
