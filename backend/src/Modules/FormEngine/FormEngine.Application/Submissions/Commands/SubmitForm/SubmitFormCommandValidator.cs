using FluentValidation;
using FormEngine.Domain.Constants;

namespace FormEngine.Application.Submissions.Commands.SubmitForm;

public sealed class SubmitFormCommandValidator : AbstractValidator<SubmitFormCommand>
{
    public SubmitFormCommandValidator()
    {
        RuleFor(x => x.FormDefinitionId).NotEmpty().WithMessage("Form id is required.");

        RuleFor(x => x.VersionNo)
            .GreaterThan(0).WithMessage("Version number must be greater than 0.")
            .When(x => x.VersionNo.HasValue);

        RuleFor(x => x.ClientSubmissionId)
            .NotEqual(Guid.Empty).WithMessage("Client submission id must not be empty.")
            .When(x => x.ClientSubmissionId.HasValue);

        RuleFor(x => x.ContextType).MaximumLength(FormContextTypes.MaxLength);
        RuleFor(x => x.ContextId).MaximumLength(FormContextTypes.MaxLength);

        RuleFor(x => x)
            .Must(x => string.IsNullOrWhiteSpace(x.ContextType) == string.IsNullOrWhiteSpace(x.ContextId))
            .WithMessage("A context needs both a type and an id, or neither.");

        RuleFor(x => x.ContextType)
            .Must(type => type is null || !FormContextTypes.OwnedByModules.Contains(type.Trim()))
            .WithMessage(x => $"Fills for a {x.ContextType!.Trim()} are recorded through that module, not posted to the form directly.");

        RuleFor(x => x.Answers)
            .NotNull().WithMessage("Answers are required.")
            .Must(a => a is { Count: > 0 }).WithMessage("At least one answer is required.");
    }
}
