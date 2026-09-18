namespace Workflow.Application.Commands.SimulateWorkflowBinding;

using FluentValidation;

public sealed class SimulateWorkflowBindingCommandValidator
    : AbstractValidator<SimulateWorkflowBindingCommand>
{
    public SimulateWorkflowBindingCommandValidator()
    {
        RuleFor(x => x.BindingId).NotEmpty();
        RuleFor(x => x.OrganizationId).NotEmpty();
    }
}
