namespace Workflow.Application.Commands.ReassignWorkItem;

using FluentValidation;

public sealed class ReassignWorkItemCommandValidator : AbstractValidator<ReassignWorkItemCommand>
{
    public ReassignWorkItemCommandValidator()
    {
        RuleFor(x => x.WorkItemId).NotEmpty();
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.ActorUserId).NotEmpty();
        RuleFor(x => x.NewAssignmentGroupId).NotEmpty();
    }
}
