namespace Workflow.Application.Commands.UpdateWorkflowDefinition;

using FluentValidation;

public sealed class UpdateWorkflowDefinitionCommandValidator
    : AbstractValidator<UpdateWorkflowDefinitionCommand>
{
    public UpdateWorkflowDefinitionCommandValidator()
    {
        RuleFor(x => x.DefinitionId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.NameAr).MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(1000);
        RuleFor(x => x.DescriptionAr).MaximumLength(1000);
    }
}
