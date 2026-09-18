namespace Workflow.Application.Commands.ReplayInboxMessage;

using FluentValidation;

public sealed class ReplayInboxMessageCommandValidator : AbstractValidator<ReplayInboxMessageCommand>
{
    public ReplayInboxMessageCommandValidator()
    {
        RuleFor(x => x.MessageId).NotEmpty();
        RuleFor(x => x.OrganizationId).NotEmpty();
    }
}
