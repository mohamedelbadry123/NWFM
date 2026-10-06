namespace NWFM.Tests.Modules.Workflow;

using System.Text.Json;
using FluentAssertions;
using Moq;
using NWFM.Shared.Integration.Organization;
using NWFM.Shared.Integration.Workflow;
using NWFM.Shared.Organization;
using global::Workflow.Domain.Entities;
using global::Workflow.Infrastructure.Services;

public sealed class WorkflowDefinitionScopeTests
{
    [Fact]
    public async Task NewDraft_PreservesPublishedClassificationAndScopeWithoutChangingTheSource()
    {
        var definition = WorkflowDefinition.Create(Guid.NewGuid(), "scope-draft", "Scope draft", DateTime.UtcNow);
        var previous = WorkflowVersion.CreateDraft(definition.Id, 1, Guid.NewGuid(), DateTime.UtcNow);
        var json = JsonSerializer.Serialize(Settings("Child") with { TaskTypeId = Guid.NewGuid() }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        previous.SetWorkspace(json);
        previous.Publish(Guid.NewGuid(), DateTime.UtcNow);
        var gate = new Mock<global::Workflow.Application.Abstractions.IWorkflowFeatureGate>();
        gate.Setup(g => g.EnsureEnabled()).Returns(NWFM.Shared.Results.Result.Success());
        var definitions = new Mock<global::Workflow.Domain.Repositories.IWorkflowDefinitionRepository>();
        definitions.Setup(d => d.GetByIdAsync(definition.Id, It.IsAny<CancellationToken>())).ReturnsAsync(definition);
        var versions = new Mock<global::Workflow.Domain.Repositories.IWorkflowVersionRepository>();
        versions.Setup(v => v.GetLatestPublishedAsync(definition.Id, It.IsAny<CancellationToken>())).ReturnsAsync(previous);
        versions.Setup(v => v.GetNextVersionNumberAsync(definition.Id, It.IsAny<CancellationToken>())).ReturnsAsync(2);
        var handler = new global::Workflow.Application.Commands.CreateWorkflowDraft.CreateWorkflowDraftCommandHandler(gate.Object, definitions.Object, versions.Object);
        var result = await handler.Handle(new(definition.Id, Guid.NewGuid()), default);
        result.IsSuccess.Should().BeTrue();
        result.Value.WorkspaceJson.Should().Be(json);
        result.Value.VersionNumber.Should().Be(2);
        previous.WorkspaceJson.Should().Be(json);
        previous.IsDraft.Should().BeFalse();
    }

    private static WorkflowWorkspaceDefinition Settings(string kind = "Main") => new(kind)
    {
        SchemaVersion = 2,
        OrganizationScopes = [new(OrgLevels.Cluster, "A", "A"), new(OrgLevels.OperationArea, "OA", "B", "B1")]
    };

    [Fact]
    public void Settings_RoundTripSeveralScopesAndClassification()
    {
        var settings = Settings() with { FieldActivityTypeId = Guid.NewGuid(), DepartmentCode = "WATER", FieldActivityCode = "REPAIR" };
        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var read = WorkflowWorkspaceDefinition.Read(json)!;
        read.Should().BeEquivalentTo(settings);
        read.HasRequiredLocation.Should().BeTrue();
        read.DefaultLocation.Should().BeNull();
        read.AllowsLocation(new("B", "B1", null, "OA")).Should().BeTrue();
        read.AllowsLocation(new("B", "B1", "BR")).Should().BeFalse();
        read.AllowsLocation(new("A")).Should().BeTrue();
        read.AllowsLocation(new("C")).Should().BeFalse();
    }

    [Theory]
    [InlineData("Main")]
    [InlineData("Child")]
    public async Task Validation_AcceptsPartialDepthForBothKinds(string kind)
    {
        var directory = new Mock<IOrgDirectory>();
        directory.Setup(d => d.IsValidLocationAsync(It.IsAny<OrgLocation>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var issues = await WorkflowWorkspacePublisher.ValidateLocationAsync(Settings(kind), directory.Object, default);
        issues.Should().BeEmpty();
        directory.Verify(d => d.IsValidLocationAsync(new OrgLocation("A", null, null, null), It.IsAny<CancellationToken>()), Times.Once);
        directory.Verify(d => d.IsValidLocationAsync(new OrgLocation("B", "B1", null, "OA"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Validation_RejectsUnknownHierarchyAndEmptyOrMalformedScopes()
    {
        var directory = new Mock<IOrgDirectory>();
        (await WorkflowWorkspacePublisher.ValidateLocationAsync(Settings(), directory.Object, default)).Should().Contain(i => i.Code == "WORKSPACE_SCOPE");
        foreach (var scopes in new IReadOnlyList<WorkflowOrganizationScope>[] { [], [new("Branch", "BR", "A")], [new("Cbu", "WRONG", "A", "A1")], [new("Unknown", "A", "A")] })
            (await WorkflowWorkspacePublisher.ValidateLocationAsync(Settings() with { OrganizationScopes = scopes }, directory.Object, default))
                .Should().Contain(i => i.Code == "WORKSPACE_SCOPE_REQUIRED");
    }

    [Fact]
    public async Task Classification_UsesLookupIdAndDepartment_NotJustAnActivityCode()
    {
        var first = new WorkflowLookupItem(Guid.NewGuid(), "REPAIR", "Repair water", "", "WATER");
        var second = new WorkflowLookupItem(Guid.NewGuid(), "REPAIR", "Repair wastewater", "", "WASTE");
        var references = new Mock<IWorkflowReferenceData>();
        references.Setup(r => r.ListAsync("field-activity-types", null, It.IsAny<CancellationToken>())).ReturnsAsync([first, second]);
        var valid = Settings() with { FieldActivityTypeId = second.Id, DepartmentCode = "WASTE", FieldActivityCode = "REPAIR" };
        (await WorkflowClassificationValidator.ValidateAsync(valid, references.Object, null, default)).Should().BeEmpty();
        (await WorkflowClassificationValidator.ValidateAsync(valid with { DepartmentCode = "WATER" }, references.Object, null, default)).Should().NotBeEmpty();
        (await WorkflowClassificationValidator.ValidateAsync(valid with { FieldActivityTypeId = Guid.NewGuid() }, references.Object, null, default)).Should().NotBeEmpty();
        (await WorkflowClassificationValidator.ValidateAsync(valid with { TaskTypeId = Guid.NewGuid() }, references.Object, null, default))
            .Should().Contain(i => i.Code == "WORKSPACE_TYPE");
    }

    [Fact]
    public async Task Classification_RequiresActiveTaskTypeForChild_AndRejectsActivityType()
    {
        var catalog = new Mock<IWorkflowTaskTypeCatalog>();
        var id = Guid.NewGuid();
        catalog.Setup(c => c.IsActiveAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var child = Settings("Child") with { TaskTypeId = id };
        (await WorkflowClassificationValidator.ValidateAsync(child, null, catalog.Object, default)).Should().BeEmpty();
        (await WorkflowClassificationValidator.ValidateAsync(child with { TaskTypeId = Guid.NewGuid() }, null, catalog.Object, default)).Should().NotBeEmpty();
        (await WorkflowClassificationValidator.ValidateAsync(child with { FieldActivityTypeId = Guid.NewGuid() }, null, catalog.Object, default))
            .Should().Contain(i => i.Code == "WORKSPACE_TYPE");
    }

    [Fact]
    public void ClusterOnlyAccess_RequiresClusterCoverage_AndSqlMatchesMemory()
    {
        var hierarchy = OrgHierarchy.Build([("A1", "A")], [("BR", "A1")], []);
        var instance = WorkflowInstance.Start(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "id", "entity", "start", DateTime.UtcNow);
        instance.SetExecutionContext(new OrgLocation("A"), false);
        foreach (var (level, code, allowed) in new[] { (OrgLevels.Cluster, "A", true), (OrgLevels.Cbu, "A1", false), (OrgLevels.Branch, "BR", false), (OrgLevels.Cluster, "B", false) })
        {
            var coverage = OrgScopeSet.FromRows([new(level, code, null)], hierarchy);
            WorkflowScopeFilter.Allows(instance, coverage).Should().Be(allowed);
            WorkflowScopeFilter.ForScope(coverage).Compile()(instance).Should().Be(allowed);
        }
    }
}
