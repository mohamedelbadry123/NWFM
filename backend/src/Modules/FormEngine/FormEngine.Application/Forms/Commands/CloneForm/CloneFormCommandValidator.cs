using FluentValidation;
using FormEngine.Application.Forms.Common;

namespace FormEngine.Application.Forms.Commands.CloneForm;

public sealed class CloneFormCommandValidator : AbstractValidator<CloneFormCommand>
{
    public CloneFormCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty().WithMessage("Form id is required.");
        RuleFor(x => x.NewCode).ValidFormCode();
        RuleFor(x => x.NewNameEn).ValidFormName("English");
        RuleFor(x => x.NewNameAr).ValidFormName("Arabic");
    }
}
