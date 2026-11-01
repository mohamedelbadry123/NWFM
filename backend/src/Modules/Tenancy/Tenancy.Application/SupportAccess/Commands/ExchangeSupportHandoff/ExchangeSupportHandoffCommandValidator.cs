namespace Tenancy.Application.SupportAccess.Commands.ExchangeSupportHandoff;

public sealed class ExchangeSupportHandoffCommandValidator : AbstractValidator<ExchangeSupportHandoffCommand>
{
    private const int MaxCodeLength = 128;

    public ExchangeSupportHandoffCommandValidator()
    {
        RuleFor(x => x.Code)
            .NotEmpty().WithMessage("The handoff code is required.")
            .MaximumLength(MaxCodeLength).WithMessage($"The handoff code must not exceed {MaxCodeLength} characters.");
    }
}
