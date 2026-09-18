namespace Workflow.Application.Commands.ReplayOutboxMessage;

using FluentValidation;

public sealed class ReplayOutboxMessageCommandValidator : AbstractValidator<ReplayOutboxMessageCommand>
{
    public ReplayOutboxMessageCommandValidator()
    {
        RuleFor(x => x.MessageId).NotEmpty();
        RuleFor(x => x.OrganizationId).NotEmpty();
    }
}
