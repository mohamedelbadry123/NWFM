using FluentValidation;
using FormEngine.Application.Forms.Common;

namespace FormEngine.Application.Forms.Commands.UpdateForm;

public sealed class UpdateFormCommandValidator : AbstractValidator<UpdateFormCommand>
{
    public UpdateFormCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty().WithMessage("Form id is required.");
        RuleFor(x => x.NameEn).ValidFormName("English");
        RuleFor(x => x.NameAr).ValidFormName("Arabic");
        RuleFor(x => x.Category).ValidFormCategory();
        RuleFor(x => x.DepartmentCode).ValidDepartmentCode();
    }
}
