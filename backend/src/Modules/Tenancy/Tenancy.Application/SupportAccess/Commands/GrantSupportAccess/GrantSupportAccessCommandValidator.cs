namespace Tenancy.Application.SupportAccess.Commands.GrantSupportAccess;

public sealed class GrantSupportAccessCommandValidator : AbstractValidator<GrantSupportAccessCommand>
{
    public GrantSupportAccessCommandValidator()
    {
        RuleFor(x => x.Scope)
            .Must(scope => scope is SupportScopes.ReadOnly or SupportScopes.Full)
            .WithMessage("Scope must be 'readonly' or 'full'.");

        RuleFor(x => x.Hours)
            .GreaterThan(0).WithMessage("Duration must be at least one hour.");

        RuleFor(x => x.Reason)
            .NotEmpty().WithMessage("Say why support access is being granted.")
            .MaximumLength(SupportAccessGrant.ReasonMaxLength)
            .WithMessage($"Reason must not exceed {SupportAccessGrant.ReasonMaxLength} characters.");
    }
}
