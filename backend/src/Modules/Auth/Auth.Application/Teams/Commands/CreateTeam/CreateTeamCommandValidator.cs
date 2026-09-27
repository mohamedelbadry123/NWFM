using Auth.Application.Teams.Common;
using FluentValidation;

namespace Auth.Application.Teams.Commands.CreateTeam;

public sealed class CreateTeamCommandValidator : AbstractValidator<CreateTeamCommand>
{
    public CreateTeamCommandValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Name is required.")
            .MaximumLength(TeamRules.NameMaxLength).WithMessage($"Name must not exceed {TeamRules.NameMaxLength} characters.");

        RuleFor(x => x.UserCode)
            .NotEmpty().WithMessage("User code is required.")
            .MaximumLength(TeamRules.UserCodeMaxLength).WithMessage($"User code must not exceed {TeamRules.UserCodeMaxLength} characters.");

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("A password for the crew's login is required.")
            .MinimumLength(TeamRules.PasswordMinLength).WithMessage($"Password must be at least {TeamRules.PasswordMinLength} characters.");

        RuleFor(x => x.Mobile)
            .MaximumLength(TeamRules.MobileMaxLength).WithMessage($"Mobile must not exceed {TeamRules.MobileMaxLength} characters.");

        RuleFor(x => x.Email)
            .EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Email))
            .WithMessage("Email is not a valid address.");

        RuleFor(x => x.Scopes)
            .NotEmpty().WithMessage(TeamRules.ScopeRequiredMessage);
    }
}
