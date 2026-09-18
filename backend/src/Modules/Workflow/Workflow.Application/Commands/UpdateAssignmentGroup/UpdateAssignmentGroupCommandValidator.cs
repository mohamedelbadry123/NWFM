namespace Workflow.Application.Commands.UpdateAssignmentGroup;

using FluentValidation;
using Workflow.Application.Helpers;

public sealed class UpdateAssignmentGroupCommandValidator
    : AbstractValidator<UpdateAssignmentGroupCommand>
{
    public UpdateAssignmentGroupCommandValidator()
    {
        RuleFor(x => x.GroupId).NotEmpty();
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.NameAr).MaximumLength(200).When(x => x.NameAr is not null);
        RuleFor(x => x.Code).NotEmpty().MaximumLength(50)
            .Must(c => AssignmentGroupCodeGenerator.ValidCodePattern().IsMatch(c.Trim().ToUpperInvariant()))
            .WithMessage("Code must contain only uppercase letters, digits, underscores and hyphens (e.g. APPROVAL).");
    }
}
