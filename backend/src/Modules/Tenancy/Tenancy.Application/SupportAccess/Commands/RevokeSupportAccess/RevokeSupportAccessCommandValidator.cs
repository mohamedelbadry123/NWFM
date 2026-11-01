namespace Tenancy.Application.SupportAccess.Commands.RevokeSupportAccess;

public sealed class RevokeSupportAccessCommandValidator : AbstractValidator<RevokeSupportAccessCommand>
{
    public RevokeSupportAccessCommandValidator()
    {
        RuleFor(x => x.GrantId).NotEmpty().WithMessage("Grant id is required.");
    }
}
