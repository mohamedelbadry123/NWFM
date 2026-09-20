using FluentValidation;

namespace Auth.Application.Auth.Commands.ExchangeSsoCode;

public sealed class ExchangeSsoCodeCommandValidator : AbstractValidator<ExchangeSsoCodeCommand>
{
    public ExchangeSsoCodeCommandValidator()
    {
        RuleFor(x => x.Code)
            .NotEmpty()
            .WithMessage("SSO authorization code is required.");
    }
}
