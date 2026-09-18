namespace NWFM.Tests.Modules.Workflow;

using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NWFM.Shared.Abstractions;
using global::Workflow.Domain.Entities;
using global::Workflow.Infrastructure.Persistence;
using global::Workflow.Infrastructure.Persistence.Repositories;

public sealed class WorkflowDefinitionTenantIsolationTests : IDisposable
{
    private readonly Guid _orgA = Guid.NewGuid();
    private readonly Guid _orgB = Guid.NewGuid();
    private readonly WorkflowDbContext _db;
    private readonly WorkflowDefinitionRepository _repo;
    private readonly WorkflowDefinition _defA;
    private readonly WorkflowDefinition _defB;

    public WorkflowDefinitionTenantIsolationTests()
    {
        var opts = new DbContextOptionsBuilder<WorkflowDbContext>()
            .UseInMemoryDatabase($"WfDefIdor_{Guid.NewGuid()}")
            .Options;
        _db = new WorkflowDbContext(opts, new StubTenant(_orgA));
        _repo = new WorkflowDefinitionRepository(_db);

        var now = DateTime.UtcNow;
        _defA = WorkflowDefinition.Create(_orgA, "CONSENT", "Org A Consent", now);
        _defB = WorkflowDefinition.Create(_orgB, "CONSENT", "Org B Consent", now);
        _db.WorkflowDefinitions.Add(_defA);
        _db.SaveChanges();
        using var tenantB = new WorkflowDbContext(opts, new StubTenant(_orgB));
        tenantB.WorkflowDefinitions.Add(_defB);
        tenantB.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task GetPagedAsync_ForOrgA_DoesNotReturnOrgB()
    {
        var (items, total) = await _repo.GetPagedAsync(1, 20, null, _orgA);

        total.Should().Be(1);
        items.Should().ContainSingle(d => d.Id == _defA.Id);
        items.Should().NotContain(d => d.Id == _defB.Id);
    }

    [Fact]
    public async Task KeyExistsAsync_IsPerOrganization()
    {
        var existsA = await _repo.KeyExistsAsync(_orgA, "CONSENT");
        var existsB = await _repo.KeyExistsAsync(_orgB, "CONSENT");
        var missing = await _repo.KeyExistsAsync(_orgA, "DSAR");

        existsA.Should().BeTrue();
        existsB.Should().BeFalse("the current tenant cannot inspect another tenant's definition keys");
        missing.Should().BeFalse();
    }

    [Fact]
    public async Task QueryFilter_HidesOtherTenantWhenNotIgnoringFilters()
    {
        var visible = await _db.WorkflowDefinitions.ToListAsync();

        visible.Should().ContainSingle(d => d.Id == _defA.Id);
        visible.Should().NotContain(d => d.Id == _defB.Id);
    }

    private sealed class StubTenant(Guid orgId) : ICurrentTenant
    {
        public Guid OrganizationId => orgId;
    }
}
