using FluentValidation;

namespace Auth.Application.Auth.Commands.ResendTeamOtp;

public sealed class ResendTeamOtpCommandValidator : AbstractValidator<ResendTeamOtpCommand>
{
    public ResendTeamOtpCommandValidator()
    {
        RuleFor(x => x.ChallengeId)
            .NotEmpty()
            .WithMessage("Challenge ID is required.");
    }
}
