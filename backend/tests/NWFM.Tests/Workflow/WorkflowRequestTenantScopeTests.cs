namespace NWFM.Tests.Modules.Workflow;

using FluentAssertions;
using global::Workflow.Application.Queries.ListWorkflowRequests;
using global::Workflow.Domain.Entities;
using global::Workflow.Domain.Enums;

public sealed class WorkflowRequestTenantScopeTests
{
    [Fact]
    public void WorkflowRequest_OrganizationId_IsSetOnCreate()
    {
        var orgId = Guid.NewGuid();
        var request = WorkflowRequest.Create(
            orgId, "WF-2026-000001", Guid.NewGuid(), Guid.NewGuid(),
            "ConsentRequest", "entity-1", "Consent", "Consent Request",
            DateTime.UtcNow, WorkflowInstanceStatus.Running, "ConsentRequest.Submitted", DateTime.UtcNow);

        request.OrganizationId.Should().Be(orgId);
    }

    [Fact]
    public void ListWorkflowRequestsQuery_CarriesOrganizationId()
    {
        var orgId = Guid.NewGuid();
        var query = new ListWorkflowRequestsQuery(OrganizationId: orgId);

        query.OrganizationId.Should().Be(orgId);
        query.PageNumber.Should().Be(1);
    }
}
