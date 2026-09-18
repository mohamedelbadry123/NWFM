namespace Workflow.Application.Commands.CreateWorkflowDraft;

using FluentValidation;

public sealed class CreateWorkflowDraftCommandValidator
    : AbstractValidator<CreateWorkflowDraftCommand>
{
    public CreateWorkflowDraftCommandValidator()
    {
        RuleFor(x => x.DefinitionId).NotEmpty();
        RuleFor(x => x.CreatedByUserId).NotEmpty();
        RuleFor(x => x.ChangeSummary).MaximumLength(500);
    }
}
