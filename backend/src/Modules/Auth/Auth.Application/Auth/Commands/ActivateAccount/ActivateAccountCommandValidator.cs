using FluentValidation;

namespace Auth.Application.Auth.Commands.ActivateAccount;

public sealed class ActivateAccountCommandValidator : AbstractValidator<ActivateAccountCommand>
{
    public ActivateAccountCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty().WithMessage("User id is required.");
        RuleFor(x => x.Token).NotEmpty().WithMessage("Activation token is required.");
        RuleFor(x => x.NewPassword)
            .NotEmpty().WithMessage("Password is required.")
            .MaximumLength(ActivateAccountCommand.PasswordMaxLength)
            .WithMessage($"Password must not exceed {ActivateAccountCommand.PasswordMaxLength} characters.");
    }
}
