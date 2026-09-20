using FluentValidation;

namespace Auth.Application.Users.Commands.SetUserStatus;

public sealed class SetUserStatusCommandValidator : AbstractValidator<SetUserStatusCommand>
{
    public SetUserStatusCommandValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty()
            .WithMessage("User ID is required.");
    }
}
