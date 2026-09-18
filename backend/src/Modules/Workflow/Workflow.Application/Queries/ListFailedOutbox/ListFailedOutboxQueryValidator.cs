namespace Workflow.Application.Queries.ListFailedOutbox;

using FluentValidation;

public sealed class ListFailedOutboxQueryValidator : AbstractValidator<ListFailedOutboxQuery>
{
    public ListFailedOutboxQueryValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.PageNumber).GreaterThan(0);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
    }
}
