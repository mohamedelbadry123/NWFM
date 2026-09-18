namespace Workflow.Application.Commands.CreateAssignmentGroup;

using FluentValidation;
using Workflow.Application.Helpers;

public sealed class CreateAssignmentGroupCommandValidator
    : AbstractValidator<CreateAssignmentGroupCommand>
{
    public CreateAssignmentGroupCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.NameAr).MaximumLength(200).When(x => x.NameAr is not null);
        RuleFor(x => x.Code)
            .MaximumLength(50)
            .Must(c => string.IsNullOrWhiteSpace(c) || AssignmentGroupCodeGenerator.ValidCodePattern().IsMatch(c.Trim().ToUpperInvariant()))
            .WithMessage("Code must contain only uppercase letters, digits, underscores and hyphens (e.g. APPROVAL).")
            .When(x => !string.IsNullOrWhiteSpace(x.Code));
    }
}
