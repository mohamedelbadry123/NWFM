using FluentValidation;
using FormEngine.Domain.Constants;
using FormEngine.Domain.Entities;

namespace FormEngine.Application.Uploads.Commands.UploadFormFile;

public sealed class UploadFormFileCommandValidator : AbstractValidator<UploadFormFileCommand>
{
    public UploadFormFileCommandValidator()
    {
        RuleFor(x => x.FormDefinitionId).NotEmpty().WithMessage("Form id is required.");

        RuleFor(x => x.VersionNo)
            .GreaterThan(0).WithMessage("Version number must be greater than 0.")
            .When(x => x.VersionNo.HasValue);

        RuleFor(x => x.DataName)
            .NotEmpty().WithMessage("The field data name is required.")
            .MaximumLength(SubmissionFile.DataNameMaxLength);

        RuleFor(x => x.FileName)
            .NotEmpty().WithMessage("A file name is required.")
            .MaximumLength(SubmissionFile.FileNameMaxLength);

        RuleFor(x => x.SizeBytes).GreaterThan(0).WithMessage("An empty file cannot be uploaded.");

        RuleFor(x => x.ContextType).MaximumLength(FormContextTypes.MaxLength);
        RuleFor(x => x.ContextId).MaximumLength(FormContextTypes.MaxLength);

        RuleFor(x => x)
            .Must(x => string.IsNullOrWhiteSpace(x.ContextType) == string.IsNullOrWhiteSpace(x.ContextId))
            .WithMessage("A context needs both a type and an id, or neither.");
    }
}
