using FluentValidation;

namespace FormEngine.Application.Forms.Commands.DeprecateForm;

public sealed class DeprecateFormCommandValidator : AbstractValidator<DeprecateFormCommand>
{
    public DeprecateFormCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty().WithMessage("Form id is required.");
    }
}
