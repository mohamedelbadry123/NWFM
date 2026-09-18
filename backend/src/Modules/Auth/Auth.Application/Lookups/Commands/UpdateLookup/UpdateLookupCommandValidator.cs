using FluentValidation;

namespace Auth.Application.Lookups.Commands.UpdateLookup;

public sealed class UpdateLookupCommandValidator : AbstractValidator<UpdateLookupCommand>
{
    public UpdateLookupCommandValidator()
    {
        RuleFor(x => x.LookupType).NotEmpty();
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.NameEn).NotEmpty().MaximumLength(200);
        RuleFor(x => x.NameAr).NotEmpty().MaximumLength(200);
    }
}
