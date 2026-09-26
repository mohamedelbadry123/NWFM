using FluentValidation;

namespace FormEngine.Application.Forms.Commands.PublishForm;

public sealed class PublishFormCommandValidator : AbstractValidator<PublishFormCommand>
{
    public PublishFormCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty().WithMessage("Form id is required.");
    }
}
