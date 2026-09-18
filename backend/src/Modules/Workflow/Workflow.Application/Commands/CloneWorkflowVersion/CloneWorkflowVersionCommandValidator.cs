namespace Workflow.Application.Commands.CloneWorkflowVersion;

using FluentValidation;

public sealed class CloneWorkflowVersionCommandValidator
    : AbstractValidator<CloneWorkflowVersionCommand>
{
    public CloneWorkflowVersionCommandValidator()
    {
        RuleFor(x => x.SourceVersionId).NotEmpty();
        RuleFor(x => x.CreatedByUserId).NotEmpty();
        RuleFor(x => x.ChangeSummary).MaximumLength(500);
    }
}
