namespace Workflow.Application.Commands.CreateWorkflowBindingAssignmentMapping;

using FluentValidation;

public sealed class CreateWorkflowBindingAssignmentMappingCommandValidator
    : AbstractValidator<CreateWorkflowBindingAssignmentMappingCommand>
{
    public CreateWorkflowBindingAssignmentMappingCommandValidator()
    {
        RuleFor(x => x.BindingId).NotEmpty();
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.AssignmentKey).NotEmpty().MaximumLength(100)
            .Matches(@"^[A-Z0-9_]+$").WithMessage("AssignmentKey must be UPPER_SNAKE_CASE (A-Z, 0-9, underscore).");
        RuleFor(x => x.AssignmentGroupId).NotEmpty();
    }
}
