namespace Workflow.Application.Queries.ListDeadLetterInbox;

using FluentValidation;

public sealed class ListDeadLetterInboxQueryValidator : AbstractValidator<ListDeadLetterInboxQuery>
{
    public ListDeadLetterInboxQueryValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.PageNumber).GreaterThan(0);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
    }
}
