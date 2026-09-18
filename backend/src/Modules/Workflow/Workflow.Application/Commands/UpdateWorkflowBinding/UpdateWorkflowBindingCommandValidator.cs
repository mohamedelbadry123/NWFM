namespace Workflow.Application.Commands.UpdateWorkflowBinding;

using FluentValidation;

public sealed class UpdateWorkflowBindingCommandValidator : AbstractValidator<UpdateWorkflowBindingCommand>
{
    public UpdateWorkflowBindingCommandValidator()
    {
        RuleFor(x => x.BindingId).NotEmpty();
        RuleFor(x => x.ModuleKey).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EntityType).NotEmpty().MaximumLength(200);
        RuleFor(x => x.TriggerEvent).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(1000).When(x => x.Description is not null);
        RuleFor(x => x.ScreenKey).MaximumLength(200).When(x => x.ScreenKey is not null);
        RuleFor(x => x.StartEventKey).MaximumLength(200).When(x => x.StartEventKey is not null);
        RuleFor(x => x.StartConditionExpression).MaximumLength(2000).When(x => x.StartConditionExpression is not null);
    }
}
