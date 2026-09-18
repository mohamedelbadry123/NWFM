namespace NWFM.Tests.Modules.Workflow;

using System.Reflection;
using FluentAssertions;
using FluentValidation;
using Moq;
using NWFM.Shared.Results;
using global::Workflow.Application.Abstractions;
using global::Workflow.Application.Commands.ActivateWorkflowBinding;
using global::Workflow.Application.Commands.CreateWorkflowBinding;
using global::Workflow.Application.Commands.CreateWorkflowDefinition;
using global::Workflow.Application.Commands.ValidateWorkflowVersion;
using global::Workflow.Application.Constants;
using global::Workflow.Application.Queries.GetWorkflowBindingReadiness;
using global::Workflow.Application.Queries.ListWorkflowDefinitions;
using global::Workflow.Domain.Entities;
using global::Workflow.Domain.Enums;
using global::Workflow.Domain.Repositories;
using global::Workflow.Infrastructure.Services;

/// <summary>
/// Full org-owned workflow cycle: pick a company, create a definition, assign a
/// real group on each User Task, bind to a screen, then activate when every
/// User Task resolves to an active group in that same organization.
/// </summary>
public sealed class OrgOwnedWorkflowCycleTests
{
    private static readonly Guid OrgA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid OrgB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid GroupAId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private readonly Mock<IWorkflowFeatureGate> _gate = new();
    private readonly Mock<IWorkflowDefinitionRepository> _definitions = new();
    private readonly Mock<IWorkflowBindingRepository> _bindings = new();
    private readonly Mock<IWorkflowVersionRepository> _versions = new();
    private readonly Mock<IWorkflowAssignmentGroupRepository> _groups = new();
    private readonly Mock<IWorkflowBindingAssignmentMappingRepository> _mappings = new();

    public OrgOwnedWorkflowCycleTests()
    {
        _gate.Setup(g => g.EnsureEnabled()).Returns(Result.Success());
    }

    private static string MinimalXml(string? userTaskAttrs = null) =>
        $"""
        <Workflow xmlns="https://privora.io/workflow/v1">
          <Activities>
            <Activity nodeKey="start" type="Start" name="Start" />
            <Activity nodeKey="review" type="UserTask" name="Privacy Review"{(userTaskAttrs is null ? "" : " " + userTaskAttrs)} />
            <Activity nodeKey="end" type="End" name="End" />
          </Activities>
          <Transitions>
            <Transition key="t1" from="start" to="review" />
            <Transition key="t2" from="review" to="end" />
          </Transitions>
        </Workflow>
        """;

    private static WorkflowAssignmentGroup ActiveGroup(Guid orgId, string code = "PRIVACY_REVIEW")
        => WorkflowAssignmentGroup.Create(
            orgId, code, "Privacy Review", AssignmentStrategy.RoundRobin, DateTime.UtcNow);

    private static void AddActivity(WorkflowVersion version, ActivityDefinition activity)
    {
        var field = typeof(WorkflowVersion).GetField("_activities", BindingFlags.NonPublic | BindingFlags.Instance);
        var list = (System.Collections.IList)field!.GetValue(version)!;
        list.Add(activity);
    }

    private static void AddRule(ActivityDefinition activity, ActivityAssignmentRule rule)
    {
        var field = typeof(ActivityDefinition).GetField("_assignmentRules", BindingFlags.NonPublic | BindingFlags.Instance);
        var list = (System.Collections.IList)field!.GetValue(activity)!;
        list.Add(rule);
    }

    private static WorkflowVersion PublishedVersionWithUserTask(
        Guid definitionId, Guid? assignmentGroupId, string? assignmentKey)
    {
        var version = WorkflowVersion.CreateDraft(definitionId, 1, Guid.NewGuid(), DateTime.UtcNow);
        version.Publish(Guid.NewGuid(), DateTime.UtcNow);
        var task = ActivityDefinition.Create(version.Id, "review", ActivityType.UserTask, "Privacy Review", DateTime.UtcNow);
        if (assignmentGroupId.HasValue || !string.IsNullOrWhiteSpace(assignmentKey))
        {
            AddRule(task, ActivityAssignmentRule.Create(
                task.Id, AssigneeType.AssignmentGroup, 1, false, DateTime.UtcNow,
                assignmentKey: assignmentKey, referenceId: assignmentGroupId));
        }
        AddActivity(version, task);
        return version;
    }

    // ── 1. Create definition (org-owned) ────────────────────────────────────

    [Fact]
    public async Task CreateDefinition_StoresOrganizationId()
    {
        _definitions.Setup(r => r.KeyExistsAsync(OrgA, "CONSENT", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var handler = new CreateWorkflowDefinitionCommandHandler(_gate.Object, _definitions.Object);
        var result = await handler.Handle(
            new CreateWorkflowDefinitionCommand(OrgA, "CONSENT", "Consent Approval", "موافقة", null, null),
            default);

        result.IsSuccess.Should().BeTrue();
        result.Value.OrganizationId.Should().Be(OrgA);
        result.Value.DefinitionKey.Should().Be("CONSENT");
        _definitions.Verify(r => r.AddAsync(
            It.Is<WorkflowDefinition>(d => d.OrganizationId == OrgA && d.DefinitionKey == "CONSENT"),
            It.IsAny<CancellationToken>()), Times.Once);
        _definitions.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateDefinition_DuplicateKeyInSameOrg_Fails()
    {
        _definitions.Setup(r => r.KeyExistsAsync(OrgA, "CONSENT", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var handler = new CreateWorkflowDefinitionCommandHandler(_gate.Object, _definitions.Object);
        var result = await handler.Handle(
            new CreateWorkflowDefinitionCommand(OrgA, "CONSENT", "Consent", null, null, null),
            default);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(WorkflowErrors.Definition.DuplicateKey);
        _definitions.Verify(r => r.AddAsync(It.IsAny<WorkflowDefinition>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateDefinition_SameKeyInOtherOrg_Succeeds()
    {
        _definitions.Setup(r => r.KeyExistsAsync(OrgA, "CONSENT", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _definitions.Setup(r => r.KeyExistsAsync(OrgB, "CONSENT", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var handler = new CreateWorkflowDefinitionCommandHandler(_gate.Object, _definitions.Object);
        var result = await handler.Handle(
            new CreateWorkflowDefinitionCommand(OrgB, "CONSENT", "Consent B", null, null, null),
            default);

        result.IsSuccess.Should().BeTrue();
        result.Value.OrganizationId.Should().Be(OrgB);
    }

    [Fact]
    public void CreateDefinitionValidator_RequiresOrganizationId()
    {
        var validator = new CreateWorkflowDefinitionCommandValidator();
        var result = validator.Validate(
            new CreateWorkflowDefinitionCommand(Guid.Empty, "CONSENT", "Name", null, null, null));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(CreateWorkflowDefinitionCommand.OrganizationId));
    }

    [Fact]
    public async Task CreateDefinition_WhenModuleDisabled_Fails()
    {
        _gate.Setup(g => g.EnsureEnabled()).Returns(Result.Failure(WorkflowErrors.ModuleDisabled));
        var handler = new CreateWorkflowDefinitionCommandHandler(_gate.Object, _definitions.Object);

        var result = await handler.Handle(
            new CreateWorkflowDefinitionCommand(OrgA, "CONSENT", "Consent", null, null, null),
            default);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(WorkflowErrors.ModuleDisabled);
    }

    [Fact]
    public async Task ListDefinitions_PassesOrganizationFilter()
    {
        var def = WorkflowDefinition.Create(OrgA, "CONSENT", "Consent", DateTime.UtcNow);
        _definitions.Setup(r => r.GetPagedAsync(1, 20, null, OrgA, It.IsAny<CancellationToken>()))
            .ReturnsAsync(((IReadOnlyList<WorkflowDefinition>)[def], 1));
        _definitions.Setup(r => r.GetVersionCountAsync(def.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        var handler = new ListWorkflowDefinitionsQueryHandler(_gate.Object, _definitions.Object);
        var result = await handler.Handle(new ListWorkflowDefinitionsQuery(1, 20, null, OrgA), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Should().ContainSingle(d => d.OrganizationId == OrgA);
        _definitions.Verify(r => r.GetPagedAsync(1, 20, null, OrgA, It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── 2. Designer XML → compiled assignment rule ──────────────────────────

    [Fact]
    public void Compiler_UserTaskWithGroupId_SynthesizesOrgGroupRule()
    {
        var compiler = new WorkflowXmlCompiler();
        var result = compiler.Compile(MinimalXml($"""assignmentGroupId="{GroupAId}" assignmentKey="PRIVACY_REVIEW" """), out _);

        result.IsSuccess.Should().BeTrue();
        var review = result.Value!.Activities.Should().ContainSingle(a => a.NodeKey == "review").Subject;
        review.AssignmentGroupId.Should().Be(GroupAId.ToString());
        review.AssignmentRules.Should().ContainSingle();
        review.AssignmentRules[0].ReferenceId.Should().Be(GroupAId.ToString());
        review.AssignmentRules[0].AssignmentKey.Should().Be("PRIVACY_REVIEW");
        review.AssignmentRules[0].AssigneeTypeName.Should().Be("AssignmentGroup");
    }

    [Fact]
    public async Task Validate_UserTaskWithoutGroup_ReturnsUserTaskNoAssignment()
    {
        var version = WorkflowVersion.CreateDraft(Guid.NewGuid(), 1, Guid.NewGuid(), DateTime.UtcNow);
        version.UpdateXml(MinimalXml(), "hash", DateTime.UtcNow);
        _versions.Setup(r => r.GetByIdWithProjectionAsync(version.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(version);

        var handler = new ValidateWorkflowVersionCommandHandler(_gate.Object, _versions.Object, new WorkflowXmlCompiler());
        var result = await handler.Handle(new ValidateWorkflowVersionCommand(version.Id), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.IsValid.Should().BeFalse();
        result.Value.Errors.Should().Contain(e => e.Code == "USER_TASK_NO_ASSIGNMENT");
    }

    [Fact]
    public async Task Validate_UserTaskWithOrgGroup_IsValid()
    {
        var version = WorkflowVersion.CreateDraft(Guid.NewGuid(), 1, Guid.NewGuid(), DateTime.UtcNow);
        version.UpdateXml(MinimalXml($"""assignmentGroupId="{GroupAId}" """), "hash", DateTime.UtcNow);
        _versions.Setup(r => r.GetByIdWithProjectionAsync(version.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(version);

        var handler = new ValidateWorkflowVersionCommandHandler(_gate.Object, _versions.Object, new WorkflowXmlCompiler());
        var result = await handler.Handle(new ValidateWorkflowVersionCommand(version.Id), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.IsValid.Should().BeTrue();
        result.Value.Errors.Should().BeEmpty();
    }

    // ── 3. Binding attaches the org-owned process to a screen ───────────────

    [Fact]
    public async Task CreateBinding_WhenDefinitionBelongsToOtherOrg_Fails()
    {
        var definition = WorkflowDefinition.Create(OrgA, "CONSENT", "Consent", DateTime.UtcNow);
        _definitions.Setup(r => r.GetByIdAsync(definition.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(definition);

        var handler = new CreateWorkflowBindingCommandHandler(_gate.Object, _definitions.Object, _bindings.Object);
        var result = await handler.Handle(BindingCommand(definition.Id, OrgB), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(WorkflowErrors.Binding.OrganizationMismatch);
        _bindings.Verify(r => r.AddAsync(It.IsAny<WorkflowBinding>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateBinding_WhenDefinitionMatchesOrg_Succeeds()
    {
        var definition = WorkflowDefinition.Create(OrgA, "CONSENT", "Consent", DateTime.UtcNow);
        _definitions.Setup(r => r.GetByIdAsync(definition.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(definition);
        _bindings.Setup(r => r.BindingExistsAsync(
                definition.Id, OrgA, "Consent", "ConsentRequest", "Created", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var handler = new CreateWorkflowBindingCommandHandler(_gate.Object, _definitions.Object, _bindings.Object);
        var result = await handler.Handle(BindingCommand(definition.Id, OrgA), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.OrganizationId.Should().Be(OrgA);
        result.Value.WorkflowDefinitionId.Should().Be(definition.Id);
        result.Value.ScreenKey.Should().Be("consent-requests");
        _bindings.Verify(r => r.AddAsync(
            It.Is<WorkflowBinding>(b => b.OrganizationId == OrgA && b.ScreenKey == "consent-requests"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateBinding_WhenDefinitionMissing_Fails()
    {
        _definitions.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkflowDefinition?)null);

        var handler = new CreateWorkflowBindingCommandHandler(_gate.Object, _definitions.Object, _bindings.Object);
        var result = await handler.Handle(BindingCommand(Guid.NewGuid(), OrgA), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(WorkflowErrors.Definition.NotFound);
    }

    private static CreateWorkflowBindingCommand BindingCommand(Guid definitionId, Guid orgId) =>
        new(definitionId, orgId, "Consent", "ConsentRequest", "Created",
            "Consent screen", WorkflowBindingMode.Disabled, WorkflowVersionPolicy.Latest,
            null, null, null, "consent-requests");

    // ── 4. Runtime resolver: group id, then code, then legacy mapping ───────

    [Fact]
    public async Task Resolver_PrefersAssignmentGroupIdInSameOrg()
    {
        var group = ActiveGroup(OrgA);
        _groups.Setup(r => r.GetByIdAsync(group.Id, OrgA, It.IsAny<CancellationToken>()))
            .ReturnsAsync(group);

        var resolver = new WorkflowAssignmentResolver(_mappings.Object, _groups.Object);
        var result = await resolver.ResolveGroupAsync(OrgA, Guid.NewGuid(), "PRIVACY_REVIEW", group.Id);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(group.Id);
        _mappings.Verify(
            r => r.GetByAssignmentKeyAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Resolver_GroupIdFromOtherOrg_Fails()
    {
        _groups.Setup(r => r.GetByIdAsync(GroupAId, OrgB, It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkflowAssignmentGroup?)null);

        var resolver = new WorkflowAssignmentResolver(_mappings.Object, _groups.Object);
        var result = await resolver.ResolveGroupAsync(OrgB, Guid.NewGuid(), "PRIVACY_REVIEW", GroupAId);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(WorkflowErrors.AssignmentGroup.NotFound);
    }

    [Fact]
    public async Task Resolver_InactiveGroupId_Fails()
    {
        var group = ActiveGroup(OrgA);
        group.Deactivate(DateTime.UtcNow);
        _groups.Setup(r => r.GetByIdAsync(group.Id, OrgA, It.IsAny<CancellationToken>()))
            .ReturnsAsync(group);

        var resolver = new WorkflowAssignmentResolver(_mappings.Object, _groups.Object);
        var result = await resolver.ResolveGroupAsync(OrgA, Guid.NewGuid(), null, group.Id);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(WorkflowErrors.AssignmentGroup.NotFound);
    }

    [Fact]
    public async Task Resolver_FallsBackToGroupCodeInOrg()
    {
        var group = ActiveGroup(OrgA);
        _groups.Setup(r => r.GetByCodeAsync("PRIVACY_REVIEW", OrgA, It.IsAny<CancellationToken>()))
            .ReturnsAsync(group);

        var resolver = new WorkflowAssignmentResolver(_mappings.Object, _groups.Object);
        var result = await resolver.ResolveGroupAsync(OrgA, Guid.NewGuid(), "PRIVACY_REVIEW", null);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(group.Id);
    }

    [Fact]
    public async Task Resolver_NoGroupAndNoKey_FailsUnmapped()
    {
        var resolver = new WorkflowAssignmentResolver(_mappings.Object, _groups.Object);
        var result = await resolver.ResolveGroupAsync(OrgA, Guid.NewGuid(), null, null);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(WorkflowErrors.Instance.AssignmentKeyNotMapped);
    }

    // ── 5. Activate / readiness require the org's group on every User Task ──

    [Fact]
    public async Task Activate_WhenDefinitionOrgDiffers_Fails()
    {
        var definition = WorkflowDefinition.Create(OrgA, "CONSENT", "Consent", DateTime.UtcNow);
        var binding = WorkflowBinding.Create(definition.Id, OrgB, "Consent", "ConsentRequest", "Created", DateTime.UtcNow);
        _bindings.Setup(r => r.GetByIdForSuperAdminAsync(binding.Id, It.IsAny<CancellationToken>())).ReturnsAsync(binding);
        _definitions.Setup(r => r.GetByIdAsync(definition.Id, It.IsAny<CancellationToken>())).ReturnsAsync(definition);

        var handler = new ActivateWorkflowBindingCommandHandler(
            _gate.Object, _bindings.Object, _versions.Object, _groups.Object, _definitions.Object);
        var result = await handler.Handle(new ActivateWorkflowBindingCommand(binding.Id), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(WorkflowErrors.Binding.OrganizationMismatch);
    }

    [Fact]
    public async Task Activate_ShadowWithoutOrgGroup_FailsIncompleteMappings()
    {
        var definition = WorkflowDefinition.Create(OrgA, "CONSENT", "Consent", DateTime.UtcNow);
        var binding = WorkflowBinding.Create(
            definition.Id, OrgA, "Consent", "ConsentRequest", "Created", DateTime.UtcNow,
            mode: WorkflowBindingMode.Shadow);
        var version = PublishedVersionWithUserTask(definition.Id, assignmentGroupId: null, assignmentKey: null);

        _bindings.Setup(r => r.GetByIdForSuperAdminAsync(binding.Id, It.IsAny<CancellationToken>())).ReturnsAsync(binding);
        _definitions.Setup(r => r.GetByIdAsync(definition.Id, It.IsAny<CancellationToken>())).ReturnsAsync(definition);
        _versions.Setup(r => r.GetLatestPublishedWithProjectionAsync(definition.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(version);

        var handler = new ActivateWorkflowBindingCommandHandler(
            _gate.Object, _bindings.Object, _versions.Object, _groups.Object, _definitions.Object);
        var result = await handler.Handle(new ActivateWorkflowBindingCommand(binding.Id), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(WorkflowErrors.Binding.IncompleteMappings);
    }

    [Fact]
    public async Task Activate_ShadowWithOrgGroup_Succeeds()
    {
        var definition = WorkflowDefinition.Create(OrgA, "CONSENT", "Consent", DateTime.UtcNow);
        var binding = WorkflowBinding.Create(
            definition.Id, OrgA, "Consent", "ConsentRequest", "Created", DateTime.UtcNow,
            mode: WorkflowBindingMode.Shadow);
        var group = ActiveGroup(OrgA);
        var version = PublishedVersionWithUserTask(definition.Id, group.Id, "PRIVACY_REVIEW");

        _bindings.Setup(r => r.GetByIdForSuperAdminAsync(binding.Id, It.IsAny<CancellationToken>())).ReturnsAsync(binding);
        _definitions.Setup(r => r.GetByIdAsync(definition.Id, It.IsAny<CancellationToken>())).ReturnsAsync(definition);
        _versions.Setup(r => r.GetLatestPublishedWithProjectionAsync(definition.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(version);
        _groups.Setup(r => r.GetByIdAsync(group.Id, OrgA, It.IsAny<CancellationToken>())).ReturnsAsync(group);

        var handler = new ActivateWorkflowBindingCommandHandler(
            _gate.Object, _bindings.Object, _versions.Object, _groups.Object, _definitions.Object);
        var result = await handler.Handle(new ActivateWorkflowBindingCommand(binding.Id), default);

        result.IsSuccess.Should().BeTrue();
        binding.IsActive.Should().BeTrue();
        _bindings.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Activate_WhenTenantFilterHidesBinding_UsesSuperAdminLookup()
    {
        var definition = WorkflowDefinition.Create(OrgA, "CONSENT", "Consent", DateTime.UtcNow);
        var binding = WorkflowBinding.Create(
            definition.Id, OrgA, "Consent", "ConsentRequest", "Created", DateTime.UtcNow,
            mode: WorkflowBindingMode.Shadow);
        var group = ActiveGroup(OrgA);
        var version = PublishedVersionWithUserTask(definition.Id, group.Id, "PRIVACY_REVIEW");

        _bindings.Setup(r => r.GetByIdAsync(binding.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkflowBinding?)null);
        _bindings.Setup(r => r.GetByIdForSuperAdminAsync(binding.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(binding);
        _definitions.Setup(r => r.GetByIdAsync(definition.Id, It.IsAny<CancellationToken>())).ReturnsAsync(definition);
        _versions.Setup(r => r.GetLatestPublishedWithProjectionAsync(definition.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(version);
        _groups.Setup(r => r.GetByIdAsync(group.Id, OrgA, It.IsAny<CancellationToken>())).ReturnsAsync(group);

        var handler = new ActivateWorkflowBindingCommandHandler(
            _gate.Object, _bindings.Object, _versions.Object, _groups.Object, _definitions.Object);
        var result = await handler.Handle(new ActivateWorkflowBindingCommand(binding.Id), default);

        result.IsSuccess.Should().BeTrue();
        binding.IsActive.Should().BeTrue();
        _bindings.Verify(r => r.GetByIdForSuperAdminAsync(binding.Id, It.IsAny<CancellationToken>()), Times.Once);
        _bindings.Verify(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Readiness_ReportsUnmappedUserTaskWhenGroupMissing()
    {
        var binding = WorkflowBinding.Create(
            Guid.NewGuid(), OrgA, "Consent", "ConsentRequest", "Created", DateTime.UtcNow);
        var version = PublishedVersionWithUserTask(binding.WorkflowDefinitionId, GroupAId, "PRIVACY_REVIEW");

        _bindings.Setup(r => r.GetByIdForSuperAdminAsync(binding.Id, It.IsAny<CancellationToken>())).ReturnsAsync(binding);
        _versions.Setup(r => r.GetLatestPublishedWithProjectionAsync(binding.WorkflowDefinitionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(version);
        _groups.Setup(r => r.GetByIdAsync(GroupAId, OrgA, It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkflowAssignmentGroup?)null);
        _groups.Setup(r => r.GetByCodeAsync("PRIVACY_REVIEW", OrgA, It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkflowAssignmentGroup?)null);

        var handler = new GetWorkflowBindingReadinessQueryHandler(
            _gate.Object, _bindings.Object, _versions.Object, _groups.Object);
        var result = await handler.Handle(new GetWorkflowBindingReadinessQuery(binding.Id), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.IsReady.Should().BeFalse();
        result.Value.UnmappedAssignmentKeys.Should().Contain("PRIVACY_REVIEW");
        result.Value.BlockingReason.Should().Contain("User tasks without an organization group");
    }

    [Fact]
    public async Task Readiness_ReadyWhenUserTaskResolvesToOrgGroup()
    {
        var binding = WorkflowBinding.Create(
            Guid.NewGuid(), OrgA, "Consent", "ConsentRequest", "Created", DateTime.UtcNow);
        var group = ActiveGroup(OrgA);
        var version = PublishedVersionWithUserTask(binding.WorkflowDefinitionId, group.Id, "PRIVACY_REVIEW");

        _bindings.Setup(r => r.GetByIdForSuperAdminAsync(binding.Id, It.IsAny<CancellationToken>())).ReturnsAsync(binding);
        _versions.Setup(r => r.GetLatestPublishedWithProjectionAsync(binding.WorkflowDefinitionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(version);
        _groups.Setup(r => r.GetByIdAsync(group.Id, OrgA, It.IsAny<CancellationToken>())).ReturnsAsync(group);

        var handler = new GetWorkflowBindingReadinessQueryHandler(
            _gate.Object, _bindings.Object, _versions.Object, _groups.Object);
        var result = await handler.Handle(new GetWorkflowBindingReadinessQuery(binding.Id), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.IsReady.Should().BeTrue();
        result.Value.UnmappedAssignmentKeys.Should().BeEmpty();
        result.Value.MappedAssignmentKeys.Should().Contain("PRIVACY_REVIEW");
        result.Value.BlockingReason.Should().BeNull();
    }
}
