namespace Workflow.Application.Commands.DelegateWorkItem;

using FluentValidation;

public sealed class DelegateWorkItemCommandValidator : AbstractValidator<DelegateWorkItemCommand>
{
    public DelegateWorkItemCommandValidator()
    {
        RuleFor(x => x.WorkItemId).NotEmpty();
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.ActorUserId).NotEmpty();
        RuleFor(x => x.DelegateToUserId).NotEmpty()
            .NotEqual(x => x.ActorUserId)
            .WithMessage("Cannot delegate a work item to yourself.");
        RuleFor(x => x.Comment).MaximumLength(4000);
    }
}
