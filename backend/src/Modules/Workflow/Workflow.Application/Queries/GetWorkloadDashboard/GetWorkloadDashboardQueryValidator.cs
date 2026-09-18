namespace Workflow.Application.Queries.GetWorkloadDashboard;

using FluentValidation;

public sealed class GetWorkloadDashboardQueryValidator : AbstractValidator<GetWorkloadDashboardQuery>
{
    public GetWorkloadDashboardQueryValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
    }
}
