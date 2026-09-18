namespace Workflow.Application.Commands.UpdateWorkflowBindingAssignmentMapping;

using FluentValidation;

public sealed class UpdateWorkflowBindingAssignmentMappingCommandValidator
    : AbstractValidator<UpdateWorkflowBindingAssignmentMappingCommand>
{
    public UpdateWorkflowBindingAssignmentMappingCommandValidator()
    {
        RuleFor(x => x.MappingId).NotEmpty();
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.AssignmentGroupId).NotEmpty();
    }
}
