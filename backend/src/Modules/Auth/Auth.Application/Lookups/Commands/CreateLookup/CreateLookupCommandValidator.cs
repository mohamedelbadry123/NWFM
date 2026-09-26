using FluentValidation;

namespace Auth.Application.Lookups.Commands.CreateLookup;

public sealed class CreateLookupCommandValidator : AbstractValidator<CreateLookupCommand>
{
    public CreateLookupCommandValidator()
    {
        RuleFor(x => x.LookupType).NotEmpty();
        RuleFor(x => x.Code).NotEmpty().MaximumLength(50);
        RuleFor(x => x.NameEn).NotEmpty().MaximumLength(200);
        RuleFor(x => x.NameAr).NotEmpty().MaximumLength(200);
    }
}
