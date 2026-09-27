using FluentValidation;

namespace Auth.Application.Auth.Commands.LoginTeam;

public sealed class LoginTeamCommandValidator : AbstractValidator<LoginTeamCommand>
{
    public LoginTeamCommandValidator()
    {
        RuleFor(x => x.UserCode)
            .NotEmpty()
            .WithMessage("User code is required.");

        RuleFor(x => x.Password)
            .NotEmpty()
            .WithMessage("Password is required.");
    }
}
