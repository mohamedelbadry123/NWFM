using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using NWFM.Shared.Abstractions;
using NWFM.Shared.Constants;
using NWFM.Shared.Integration.Forms;
using NWFM.Shared.Integration.Organization;
using NWFM.Shared.Integration.Workflow;
using NWFM.Shared.Organization;
using Workflow.Application.Integrations;
using Workflow.Application.Mapping;
using Workflow.Application.Workspace;
using Workflow.Domain.Entities;
using Workflow.Domain.Enums;
using Workflow.Infrastructure.Persistence;
using Workflow.Infrastructure.Persistence.Repositories;
using Workflow.Infrastructure.Services;

namespace NWFM.Tests.Modules.Workflow;

public sealed class DesignerMappingCatalogTests
{
    [Theory]
    [InlineData(true, true, true, true, 1)]
    [InlineData(false, true, true, true, 0)]
    [InlineData(true, false, true, true, 0)]
    [InlineData(true, true, false, true, 0)]
    [InlineData(true, true, true, false, 0)]
    public async Task CatalogRequiresTenantTerritoryDataPermissionAndFormClassification(bool sameTenant, bool territory, bool formPermission, bool formScope, int expected)
    {
        var org = Guid.NewGuid(); var tenant = Mock.Of<ICurrentTenant>(t => t.OrganizationId == org);
        var storedOrg = sameTenant ? org : Guid.NewGuid();
        // Deliberately expose a foreign tenant through the context to prove the catalog's explicit tenant boundary.
        await using var db = new WorkflowDbContext(new DbContextOptionsBuilder<WorkflowDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, Mock.Of<ICurrentTenant>(t => t.OrganizationId == storedOrg));
        var definition = WorkflowDefinition.Create(storedOrg, "mapping", "Mapping", DateTime.UtcNow);
        var version = WorkflowVersion.CreateDraft(definition.Id, 1, Guid.NewGuid(), DateTime.UtcNow);
        version.SetWorkspace(JsonSerializer.Serialize(new WorkflowWorkspaceDefinition("Main", "C", "B", DesignerVersion: 4), IntegrationJson.Options));
        version.Publish(Guid.NewGuid(), DateTime.UtcNow);
        var form = Guid.NewGuid(); var type = Guid.NewGuid();
        var node = ActivityDefinition.Create(version.Id, "step", ActivityType.UserTask, "Step", DateTime.UtcNow,
            configurationJson: JsonSerializer.Serialize(new BusinessActivityConfiguration { TaskTypeId = type, DepartmentCode = "D", FieldActivityCode = "A", FormSteps = [new() { Id = "form-step", FormId = form, VersionNo = 2, Name = "Pinned form" }] }, IntegrationJson.Options));
        db.AddRange(definition, version, node); await db.SaveChangesAsync();
        var user = new Mock<ICurrentUser>(); user.Setup(u => u.HasPermission(It.IsAny<string>())).Returns(false);
        var hierarchy = OrgHierarchy.Build([("B", "C"), ("OTHER", "C2")], [], []);
        var scope = OrgScopeSet.FromRows([new("Cbu", territory ? "B" : "OTHER", ["D"], ["A"])], hierarchy);
        var scopes = new Mock<IOrgScopeProvider>(); scopes.Setup(s => s.GetCurrentUserScopeAsync(default)).ReturnsAsync(scope); scopes.Setup(s => s.GetHierarchyAsync(default)).ReturnsAsync(hierarchy);
        var forms = new Mock<IFormGateway>(MockBehavior.Strict);
        forms.Setup(f => f.FindFieldActivityFormAsync(form, default)).ReturnsAsync(new FieldActivityFormInfo(form, "F", "Form", "Form", "", "Published", "D", formScope ? "A" : "DENIED", 2, [2], true, DateTime.UtcNow));
        forms.Setup(f => f.GetVersionSchemaAsync(form, 2, default)).ReturnsAsync("{}");
        forms.Setup(f => f.GetFieldsAsync(form, 2, default)).ReturnsAsync([new("a.b", "numeric", "Amount", null, null, [])]);
        var types = new Mock<IWorkflowTaskTypeCatalog>(); types.Setup(t => t.FormIdsAsync(type, default)).ReturnsAsync([form]);
        user.SetupGet(u => u.Id).Returns(Guid.NewGuid().ToString());
        var actors = new Mock<IWorkflowActorAccess>(); actors.Setup(a => a.GetAsync(It.IsAny<Guid>(), default)).ReturnsAsync(new WorkflowActorAccess(formPermission ? [NwfmPolicies.ManageDefinitions, NwfmPolicies.ViewInstances, NwfmPolicies.ViewSubmissions] : [NwfmPolicies.ManageDefinitions, NwfmPolicies.ViewInstances], scope, hierarchy));
        var service = new DesignerMappingCatalog(db, new WorkflowVersionRepository(db), tenant, user.Object, actors.Object, forms.Object, types.Object);
        var result = await service.GetAsync(definition.Id, version.Id, default);
        if (!sameTenant || !territory) { result.IsFailure.Should().BeTrue(); forms.Verify(f => f.GetFieldsAsync(It.IsAny<Guid>(), It.IsAny<int>(), default), Times.Never); }
        else { result.IsSuccess.Should().BeTrue(); result.Value.Sources.Count.Should().Be(expected); }
        if (expected == 1)
        {
            var field = result.Value.Sources.Single(); field.Reference.FormVersion.Should().Be(2); field.Reference.FormStepId.Should().Be("form-step");
            field.Reference.FieldKey.Should().Be("a.b"); field.Reference.JsonPointer.Should().BeEmpty(); field.Availability.Should().Be("SubmittedForm");
            JsonSerializer.Serialize(result.Value, IntegrationJson.Options).Should().NotContain("answers");
        }
        (await service.RelatedAsync(default)).Count.Should().Be(sameTenant && territory ? 1 : 0);
        actors.Setup(a => a.GetAsync(It.IsAny<Guid>(), default)).ReturnsAsync(new WorkflowActorAccess([], OrgScopeSet.Unrestricted(), hierarchy));
        user.Setup(u => u.HasPermission(It.IsAny<string>())).Returns(true);
        (await service.GetAsync(definition.Id, version.Id, default)).IsFailure.Should().BeTrue();
        (await service.RelatedAsync(default)).Should().BeEmpty();
    }
    [Fact]
    public void ScopedClassificationIsCheckedTogetherWithTerritory()
    {
        var hierarchy = OrgHierarchy.Build([("B", "C")], [], []);
        var scope = OrgScopeSet.FromRows([new("Cbu", "B", ["D"], ["A"])], hierarchy);
        var settings = new WorkflowWorkspaceDefinition("Main", "C", "B");
        DesignerMappingCatalog.Allowed(settings, scope, hierarchy, "D", "A").Should().BeTrue();
        DesignerMappingCatalog.Allowed(settings, scope, hierarchy, "OTHER", "A").Should().BeFalse();
    }
    [Fact]
    public async Task MainTreeIncludesPinnedChildNestedOutputsAndOnlyItsCallableTargets()
    {
        var org = Guid.NewGuid(); var tenant = Mock.Of<ICurrentTenant>(t => t.OrganizationId == org);
        await using var db = new WorkflowDbContext(new DbContextOptionsBuilder<WorkflowDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, tenant);
        var now = DateTime.UtcNow; var main = WorkflowDefinition.Create(org, "main", "Main", now); var child = WorkflowDefinition.Create(org, "child", "Child", now);
        var parentVersion = WorkflowVersion.CreateDraft(main.Id, 1, Guid.NewGuid(), now); var childVersion = WorkflowVersion.CreateDraft(child.Id, 2, Guid.NewGuid(), now); var taskType = Guid.NewGuid();
        parentVersion.SetWorkspace(JsonSerializer.Serialize(new WorkflowWorkspaceDefinition("Main", "C", "B", DesignerVersion: 4), IntegrationJson.Options));
        childVersion.SetWorkspace(JsonSerializer.Serialize(new WorkflowWorkspaceDefinition("Child", "C", "B", DesignerVersion: 4) { ParentDefinitionId = main.Id, ParentNodeKey = "task", TaskTypeId = taskType }, IntegrationJson.Options));
        var task = ActivityDefinition.Create(parentVersion.Id, "task", ActivityType.MainActivity, "Task Type", now,
            configurationJson: JsonSerializer.Serialize(new BusinessActivityConfiguration { VersionId = childVersion.Id, TaskTypeId = taskType }, IntegrationJson.Options));
        var outputSchema = new MappingSchema("array", Items: new("object", Properties: new() { ["value"] = new("number", Required: true) }));
        var mapping = new DesignerMappingConfiguration(1, new("GET", "/", []), [], [],
            [new("stable-output", "Result rows", outputSchema, new("literal", JsonSerializer.SerializeToElement(Array.Empty<object>()), LiteralSchema: outputSchema))], [], [], true, new("object", Properties: []));
        var ev = ActivityDefinition.Create(childVersion.Id, "event", ActivityType.ServiceTask, "Child Event", now, actionKey: "http.request", configurationJson: JsonSerializer.Serialize(new { typedMapping = mapping }, IntegrationJson.Options));
        db.AddRange(main, child, parentVersion, childVersion, task, ev); await db.SaveChangesAsync();
        var user = new Mock<ICurrentUser>(); user.Setup(u => u.HasPermission(It.IsAny<string>())).Returns(false);
        var scopes = new Mock<IOrgScopeProvider>(); scopes.Setup(s => s.GetCurrentUserScopeAsync(default)).ReturnsAsync(OrgScopeSet.Unrestricted()); scopes.Setup(s => s.GetHierarchyAsync(default)).ReturnsAsync(OrgHierarchy.Empty);
        user.SetupGet(u => u.Id).Returns(Guid.NewGuid().ToString());
        var actors = new Mock<IWorkflowActorAccess>(); actors.Setup(a => a.GetAsync(It.IsAny<Guid>(), default)).ReturnsAsync(new WorkflowActorAccess([], OrgScopeSet.Unrestricted(), OrgHierarchy.Empty, IsAdministrator: true));
        var service = new DesignerMappingCatalog(db, new WorkflowVersionRepository(db), tenant, user.Object, actors.Object, new Mock<IFormGateway>(MockBehavior.Strict).Object, Mock.Of<IWorkflowTaskTypeCatalog>());
        var result = await service.GetAsync(main.Id, parentVersion.Id, default); result.IsSuccess.Should().BeTrue();
        var source = result.Value.Sources.Single(s => s.Reference.Kind == "EventOutput"); source.Reference.WorkflowVersionId.Should().Be(childVersion.Id); source.Reference.FieldKey.Should().Be("stable-output");
        result.Value.Sources.Should().ContainSingle(s => s.Reference.Kind == "InvocationInput" && s.Reference.FieldKey == "$inputs");
        source.Breadcrumbs.Should().Equal("Main", "Task Type", "Child Event", "Outputs", "Result rows"); source.Schema.Items!.Properties!["value"].Type.Should().Be("number");
        result.Value.Targets.Single().Target.Should().Be(new CallableTarget(child.Id, childVersion.Id, "event")); result.Value.Targets.Single().Callable.Should().BeTrue();
    }
}
