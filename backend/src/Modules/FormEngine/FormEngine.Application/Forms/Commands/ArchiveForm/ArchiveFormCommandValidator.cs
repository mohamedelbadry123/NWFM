using FluentValidation;

namespace FormEngine.Application.Forms.Commands.ArchiveForm;

public sealed class ArchiveFormCommandValidator : AbstractValidator<ArchiveFormCommand>
{
    public ArchiveFormCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty().WithMessage("Form id is required.");
    }
}
