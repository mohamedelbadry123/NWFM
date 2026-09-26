using FluentValidation;
using FormEngine.Application.Forms.Common;

namespace FormEngine.Application.Forms.Commands.CreateForm;

public sealed class CreateFormCommandValidator : AbstractValidator<CreateFormCommand>
{
    public CreateFormCommandValidator()
    {
        RuleFor(x => x.Code).ValidFormCode();
        RuleFor(x => x.NameEn).ValidFormName("English");
        RuleFor(x => x.NameAr).ValidFormName("Arabic");
        RuleFor(x => x.Category).ValidFormCategory();
        RuleFor(x => x.DepartmentCode).ValidDepartmentCode();
    }
}
