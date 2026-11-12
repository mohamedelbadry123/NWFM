using FormEngine.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using NWFM.Shared.Abstractions;
using NWFM.Shared.MultiTenancy;
using Tasks.Infrastructure.Persistence;
using Workflow.Infrastructure.Persistence;

namespace NWFM.Tests.Security;

/// <summary>
/// Explicit classification makes new mapped data require a security decision. Every
/// module uses the dedicated deployment database; Workflow row filters are additional
/// defense. Dynamic FE submission tables are classified and validated by their publisher.
/// </summary>
public sealed class TenantModelInventoryTests
{
    private sealed record TenantContext(Guid OrganizationId) : ICurrentTenant;

    [Fact]
    public void WorkflowInventoryHasNoUnclassifiedOrUnfilteredEntity()
    {
        using var db = new WorkflowDbContext(new DbContextOptionsBuilder<WorkflowDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, new TenantContext(Guid.NewGuid()));
        string[] directlyScoped =
        [
            "ActivityInstance", "TransitionInstance", "WorkItem", "WorkItemCandidate", "WorkflowAssignmentGroup",
            "WorkflowBinding", "WorkflowBindingAssignmentMapping", "WorkflowDefinition", "WorkflowDepartment",
            "WorkflowEvent", "WorkflowEventReceipt", "WorkflowEventSubscription", "WorkflowExecutionToken",
            "WorkflowIncident", "WorkflowInstance", "WorkflowIntegrationConnection", "WorkflowIntegrationInbox",
            "WorkflowIntegrationJob", "WorkflowIntegrationOutbox", "WorkflowNotificationLog", "WorkflowParticipant",
            "WorkflowRequest", "WorkflowTimer", "WorkflowVariable", "WorkflowWorkspaceAction",
            "WorkflowMappingInvocation", "WorkflowFormReceiptReservation", "WorkflowRoutingScope",
            "WorkflowRoutingResult", "WorkflowRoutingActivation", "WorkflowRoutingUpgradeSnapshot",
        ];
        string[] parentScoped =
        [
            "ActivityActionDefinition", "ActivityAssignmentRule", "ActivityDefinition", "ActivityOutcomeDefinition",
            "BusinessCalendarHoliday", "BusinessCalendarPeriod", "WorkflowDepartmentMember", "WorkflowGroupMember",
            "WorkflowTransition", "WorkflowVariableDefinition", "WorkflowVersion",
        ];
        string[] deploymentReference = ["Tenant"];
        string[] deploymentSharedDefaults = ["BusinessCalendar", "SlaPolicy"];
        var entities = db.Model.GetEntityTypes().ToArray();
        Assert.Equal(directlyScoped.Concat(parentScoped).Concat(deploymentReference).Concat(deploymentSharedDefaults).Order(),
            entities.Select(e => e.ClrType.Name).Order());
        foreach (var entity in entities)
        {
            if (!deploymentReference.Contains(entity.ClrType.Name)) Assert.NotEmpty(entity.GetDeclaredQueryFilters());
            if (directlyScoped.Contains(entity.ClrType.Name))
            {
                Assert.True(typeof(ITenantAware).IsAssignableFrom(entity.ClrType));
                Assert.True(entity.FindProperty(nameof(ITenantAware.OrganizationId))!.IsConcurrencyToken);
            }
        }
    }

    [Fact]
    public void FormAndTaskEntitiesRemainExplicitlyClassifiedAsDedicatedDeploymentData()
    {
        using var forms = new FormEngineDbContext(new DbContextOptionsBuilder<FormEngineDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        using var tasks = new TasksDbContext(new DbContextOptionsBuilder<TasksDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        Assert.Equal(new[] { "FormDefinition", "FormField", "FormVersion", "SubmissionFile", "WorkflowReceiptKey" }.Order(),
            forms.Model.GetEntityTypes().Select(e => e.ClrType.Name).Order());
        Assert.Equal(new[] { "C2mActionMapping", "C2mDispatchLog", "FieldTask", "TaskAssignment",
            "TaskComputedValue", "TaskForm", "TaskStatusHistory", "TaskType", "TaskTypeForm" }.Order(),
            tasks.Model.GetEntityTypes().Select(e => e.ClrType.Name).Order());
        Assert.All(forms.Model.GetEntityTypes(), e => Assert.Equal("FormEngine", e.GetSchema()));
        Assert.All(tasks.Model.GetEntityTypes(), e => Assert.Equal("Task", e.GetSchema()));
    }
}
