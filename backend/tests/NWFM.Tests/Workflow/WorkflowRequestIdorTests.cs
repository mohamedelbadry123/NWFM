namespace NWFM.Tests.Modules.Workflow;

using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using NWFM.Shared.Abstractions;
using NWFM.Shared.Results;
using global::Workflow.Application.Abstractions;
using global::Workflow.Application.Queries.GetWorkflowRequestById;
using global::Workflow.Domain.Entities;
using global::Workflow.Domain.Enums;
using global::Workflow.Domain.Repositories;
using global::Workflow.Infrastructure.Persistence;
using global::Workflow.Infrastructure.Persistence.Repositories;

/// <summary>
/// Cross-tenant IDOR tests for WorkflowRequest at the repository layer and the
/// GetWorkflowRequestByIdQueryHandler. Uses an InMemory WorkflowDbContext with a
/// StubTenant for Org A. Seeds one request for Org A and one for Org B, then asserts
/// that Org A cannot observe Org B's rows.
/// </summary>
public sealed class WorkflowRequestIdorTests : IDisposable
{
    private readonly Guid _orgA = Guid.NewGuid();
    private readonly Guid _orgB = Guid.NewGuid();
    private readonly WorkflowDbContext _db;
    private readonly WorkflowRequestRepository _repo;
    private readonly WorkflowRequest _requestA;
    private readonly WorkflowRequest _requestB;

    public WorkflowRequestIdorTests()
    {
        var opts = new DbContextOptionsBuilder<WorkflowDbContext>()
            .UseInMemoryDatabase($"WorkflowIdor_{Guid.NewGuid()}")
            .Options;
        _db = new WorkflowDbContext(opts, new StubTenant(_orgA));
        _repo = new WorkflowRequestRepository(_db);

        var now = DateTime.UtcNow;
        _requestA = WorkflowRequest.Create(
            _orgA, "WF-A-001", Guid.NewGuid(), Guid.NewGuid(),
            "Consent", "entity-A", "Consent", "Consent Service",
            now, WorkflowInstanceStatus.Running, "Consent.Submitted", now);

        _requestB = WorkflowRequest.Create(
            _orgB, "WF-B-001", Guid.NewGuid(), Guid.NewGuid(),
            "Consent", "entity-B", "Consent", "Consent Service",
            now, WorkflowInstanceStatus.Running, "Consent.Submitted", now);

        // Both rows are added to the store; the global query filter limits reads.
        _db.WorkflowRequests.Add(_requestA);
        _db.SaveChanges();
        using var tenantB = new WorkflowDbContext(opts, new StubTenant(_orgB));
        tenantB.WorkflowRequests.Add(_requestB);
        tenantB.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    // ── Repository: GetByIdAsync ─────────────────────────────────────────────

    [Fact]
    public async Task GetByIdAsync_OwnRequest_ReturnsRow()
    {
        var result = await _repo.GetByIdAsync(_requestA.Id, _orgA);

        result.Should().NotBeNull();
        result!.Id.Should().Be(_requestA.Id);
    }

    [Fact]
    public async Task GetByIdAsync_CrossTenantId_ReturnsNull()
    {
        var result = await _repo.GetByIdAsync(_requestB.Id, _orgA);

        result.Should().BeNull("orgA must not see orgB's request when querying by Id");
    }

    [Fact]
    public async Task GetByIdAsync_CrossTenantIdWithWrongOrg_ReturnsNull()
    {
        // Simulate a caller that accidentally passes orgB's ID but is operating as orgA.
        var result = await _repo.GetByIdAsync(_requestB.Id, _orgB);

        // The global query filter (StubTenant = orgA) prevents orgB rows from loading.
        result.Should().BeNull("global query filter blocks orgB rows when tenant is orgA");
    }

    // ── Repository: GetPagedAsync ─────────────────────────────────────────────

    [Fact]
    public async Task GetPagedAsync_OrgA_ReturnsOnlyOrgARows()
    {
        var (items, total) = await _repo.GetPagedAsync(
            _orgA, 1, 50,
            search: null, status: null, service: null, currentStep: null,
            originalGroupId: null, fromUtc: null, toUtc: null,
            slaStatus: null, sortBy: null,
            asOfUtc: DateTime.UtcNow);

        total.Should().Be(1, "orgA has exactly one request seeded");
        items.Should().HaveCount(1);
        items[0].OrganizationId.Should().Be(_orgA);
    }

    [Fact]
    public async Task GetPagedAsync_OrgB_ReturnsZeroRows_WhenTenantIsOrgA()
    {
        // Even if orgB is passed as the organizationId filter parameter, the global
        // query filter (StubTenant = orgA) ensures orgB rows are invisible.
        var (items, total) = await _repo.GetPagedAsync(
            _orgB, 1, 50,
            search: null, status: null, service: null, currentStep: null,
            originalGroupId: null, fromUtc: null, toUtc: null,
            slaStatus: null, sortBy: null,
            asOfUtc: DateTime.UtcNow);

        total.Should().Be(0);
        items.Should().BeEmpty("global query filter prevents orgB rows when tenant context is orgA");
    }

    // ── Query handler ────────────────────────────────────────────────────────

    [Fact]
    public async Task GetByIdQueryHandler_OwnRequest_ReturnsSuccess()
    {
        var handler = BuildHandler();
        var query = new GetWorkflowRequestByIdQuery(_requestA.Id, _orgA);

        var result = await handler.Handle(query, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Id.Should().Be(_requestA.Id);
        result.Value.OrganizationId.Should().Be(_orgA);
    }

    [Fact]
    public async Task GetByIdQueryHandler_CrossTenantRequestId_ReturnsNotFound()
    {
        var handler = BuildHandler();
        var query = new GetWorkflowRequestByIdQuery(_requestB.Id, _orgA);

        var result = await handler.Handle(query, CancellationToken.None);

        result.IsFailure.Should().BeTrue("cross-tenant access must be denied");
        result.Error.Code.Should().Be("Workflow.Request.NotFound");
    }

    // ── Helper ───────────────────────────────────────────────────────────────

    private GetWorkflowRequestByIdQueryHandler BuildHandler()
    {
        var gate = new Mock<IWorkflowFeatureGate>();
        gate.Setup(g => g.EnsureEnabled()).Returns(Result.Success());

        var groups = new Mock<IWorkflowAssignmentGroupRepository>();
        groups.Setup(g => g.GetNamesByIdsAsync(
                It.IsAny<Guid>(),
                It.IsAny<IReadOnlyCollection<Guid>>(),
                It.IsAny<CancellationToken>()))
              .ReturnsAsync(new Dictionary<Guid, string>());

        return new GetWorkflowRequestByIdQueryHandler(gate.Object, _repo, groups.Object);
    }

    private sealed class StubTenant(Guid orgId) : ICurrentTenant
    {
        public Guid OrganizationId => orgId;
    }
}
