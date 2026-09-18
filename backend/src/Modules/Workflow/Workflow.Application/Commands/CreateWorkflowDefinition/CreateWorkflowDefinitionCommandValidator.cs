namespace Workflow.Application.Commands.CreateWorkflowDefinition;

using FluentValidation;

public sealed class CreateWorkflowDefinitionCommandValidator
    : AbstractValidator<CreateWorkflowDefinitionCommand>
{
    public CreateWorkflowDefinitionCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();

        RuleFor(x => x.DefinitionKey)
            .NotEmpty().MaximumLength(100)
            .Matches(@"^[A-Za-z0-9\-_]+$")
            .WithMessage("DefinitionKey may only contain letters, digits, hyphens, and underscores.");

        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.NameAr).MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(1000);
        RuleFor(x => x.DescriptionAr).MaximumLength(1000);
    }
}
