using FluentValidation;
using FormEngine.Application.Common.Schema;

namespace FormEngine.Application.Forms.Commands.SaveFormSchema;

public sealed class SaveFormSchemaCommandValidator : AbstractValidator<SaveFormSchemaCommand>
{
    public SaveFormSchemaCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty().WithMessage("Form id is required.");

        RuleFor(x => x.SchemaJson)
            .NotEmpty().WithMessage("A form schema is required.")
            .Must(FormSchemaParser.IsValidJson).WithMessage("The form schema must be valid JSON.")
            .Must(FormSchemaParser.HasElements).WithMessage("The form schema must contain at least one element.");
    }
}
