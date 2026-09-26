using Auth.Application.Teams.Common;
using FluentValidation;

namespace Auth.Application.Teams.Commands.UpdateTeam;

public sealed class UpdateTeamCommandValidator : AbstractValidator<UpdateTeamCommand>
{
    public UpdateTeamCommandValidator()
    {
        RuleFor(x => x.TeamId).NotEmpty();

        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Name is required.")
            .MaximumLength(TeamRules.NameMaxLength).WithMessage($"Name must not exceed {TeamRules.NameMaxLength} characters.");

        RuleFor(x => x.Mobile)
            .MaximumLength(TeamRules.MobileMaxLength).WithMessage($"Mobile must not exceed {TeamRules.MobileMaxLength} characters.");

        RuleFor(x => x.Email)
            .EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Email))
            .WithMessage("Email is not a valid address.");

        RuleFor(x => x.Scopes)
            .NotEmpty().WithMessage(TeamRules.ScopeRequiredMessage);
    }
}
