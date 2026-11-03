namespace Workflow.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NWFM.Shared.Abstractions;
using NWFM.Shared.Persistence;
using Workflow.Application.Settings;
using Workflow.Domain.Entities;

public sealed partial class WorkflowDbContext : BaseDbContext
{
    private readonly int _maximumPendingPerQueue;

    public WorkflowDbContext(DbContextOptions<WorkflowDbContext> options, ICurrentTenant currentTenant,
        IOptions<WorkflowSettings>? settings = null)
        : base(options, currentTenant)
    {
        var configured = settings?.Value;
        _maximumPendingPerQueue = configured?.MaxPendingPerQueueByTenant.GetValueOrDefault(currentTenant.OrganizationId.ToString("D"))
            is { } perTenant and > 0 ? perTenant : configured?.MaxPendingPerQueue ?? 10000;
        if (_maximumPendingPerQueue is < 1 or > 100000)
            throw new InvalidOperationException("Workflow queue capacity must be between 1 and 100000.");
    }

    public DbSet<WorkflowParticipant> Participants => Set<WorkflowParticipant>();
    public DbSet<WorkflowWorkspaceAction> WorkspaceActions => Set<WorkflowWorkspaceAction>();
    public DbSet<WorkflowAssignmentGroup> AssignmentGroups => Set<WorkflowAssignmentGroup>();
    public DbSet<WorkflowGroupMember> GroupMembers => Set<WorkflowGroupMember>();
    public DbSet<WorkflowDepartment> Departments => Set<WorkflowDepartment>();
    public DbSet<WorkflowDepartmentMember> DepartmentMembers => Set<WorkflowDepartmentMember>();
    public DbSet<WorkflowDefinition> WorkflowDefinitions => Set<WorkflowDefinition>();
    public DbSet<WorkflowVersion> WorkflowVersions => Set<WorkflowVersion>();
    public DbSet<ActivityDefinition> ActivityDefinitions => Set<ActivityDefinition>();
    public DbSet<WorkflowTransition> WorkflowTransitions => Set<WorkflowTransition>();
    public DbSet<WorkflowVariableDefinition> WorkflowVariableDefinitions => Set<WorkflowVariableDefinition>();
    public DbSet<ActivityAssignmentRule> ActivityAssignmentRules => Set<ActivityAssignmentRule>();
    public DbSet<ActivityOutcomeDefinition> ActivityOutcomeDefinitions => Set<ActivityOutcomeDefinition>();
    public DbSet<ActivityActionDefinition> ActivityActionDefinitions => Set<ActivityActionDefinition>();
    public DbSet<WorkflowBinding> WorkflowBindings => Set<WorkflowBinding>();
    public DbSet<WorkflowBindingAssignmentMapping> WorkflowBindingAssignmentMappings => Set<WorkflowBindingAssignmentMapping>();

    // Runtime entities
    public DbSet<WorkflowInstance> WorkflowInstances => Set<WorkflowInstance>();
    public DbSet<ActivityInstance> ActivityInstances => Set<ActivityInstance>();
    public DbSet<WorkItem> WorkItems => Set<WorkItem>();
    public DbSet<WorkItemCandidate> WorkItemCandidates => Set<WorkItemCandidate>();
    public DbSet<WorkflowVariable> WorkflowVariables => Set<WorkflowVariable>();
    public DbSet<WorkflowEvent> WorkflowEvents => Set<WorkflowEvent>();
    public DbSet<TransitionInstance> TransitionInstances => Set<TransitionInstance>();

    // Advanced runtime / configuration entities
    public DbSet<WorkflowTimer> WorkflowTimers => Set<WorkflowTimer>();
    public DbSet<WorkflowIncident> WorkflowIncidents => Set<WorkflowIncident>();
    public DbSet<BusinessCalendar> BusinessCalendars => Set<BusinessCalendar>();
    public DbSet<BusinessCalendarPeriod> BusinessCalendarPeriods => Set<BusinessCalendarPeriod>();
    public DbSet<BusinessCalendarHoliday> BusinessCalendarHolidays => Set<BusinessCalendarHoliday>();
    public DbSet<SlaPolicy> SlaPolicies => Set<SlaPolicy>();
    public DbSet<WorkflowExecutionToken> WorkflowExecutionTokens => Set<WorkflowExecutionToken>();
    public DbSet<WorkflowIntegrationOutbox> WorkflowIntegrationOutbox => Set<WorkflowIntegrationOutbox>();
    public DbSet<WorkflowIntegrationInbox> WorkflowIntegrationInbox => Set<WorkflowIntegrationInbox>();
    public DbSet<WorkflowNotificationLog> WorkflowNotificationLogs => Set<WorkflowNotificationLog>();
    public DbSet<WorkflowRequest> WorkflowRequests => Set<WorkflowRequest>();
    public DbSet<WorkflowIntegrationConnection> IntegrationConnections => Set<WorkflowIntegrationConnection>();
    public DbSet<WorkflowIntegrationJob> IntegrationJobs => Set<WorkflowIntegrationJob>();
    public DbSet<WorkflowEventSubscription> EventSubscriptions => Set<WorkflowEventSubscription>();
    public DbSet<WorkflowEventReceipt> EventReceipts => Set<WorkflowEventReceipt>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasDefaultSchema("Workflow");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(WorkflowDbContext).Assembly);

        modelBuilder.Entity<NWFM.Shared.MultiTenancy.Tenant>(tenant =>
        {
            tenant.ToTable("Tenants", "Tenancy");
            tenant.HasKey(t => t.Id);
            tenant.Property(t => t.Name).HasMaxLength(200).IsRequired();
        });

        // Member entities are not ITenantAware; match parent tenant filters so required
        // navigations to filtered principals do not return unexpected orphan rows.
        modelBuilder.Entity<WorkflowDepartmentMember>()
            .HasQueryFilter(m => m.Department.OrganizationId == CurrentTenant.OrganizationId);

        modelBuilder.Entity<WorkflowGroupMember>()
            .HasQueryFilter(m => m.AssignmentGroup.OrganizationId == CurrentTenant.OrganizationId);

        modelBuilder.Entity<WorkflowVersion>().HasQueryFilter(v => WorkflowDefinitions.Any(d => d.Id == v.WorkflowDefinitionId));
        modelBuilder.Entity<ActivityDefinition>().HasQueryFilter(a => WorkflowVersions.Any(v => v.Id == a.WorkflowVersionId));
        modelBuilder.Entity<WorkflowTransition>().HasQueryFilter(t => WorkflowVersions.Any(v => v.Id == t.WorkflowVersionId));
        modelBuilder.Entity<WorkflowVariableDefinition>().HasQueryFilter(v => WorkflowVersions.Any(w => w.Id == v.WorkflowVersionId));
        modelBuilder.Entity<ActivityAssignmentRule>().HasQueryFilter(a => ActivityDefinitions.Any(d => d.Id == a.ActivityDefinitionId));
        modelBuilder.Entity<ActivityOutcomeDefinition>().HasQueryFilter(a => WorkflowVersions.Any(v => v.Id == a.WorkflowVersionId));
        modelBuilder.Entity<ActivityActionDefinition>().HasQueryFilter(a => WorkflowVersions.Any(v => v.Id == a.WorkflowVersionId));
        modelBuilder.Entity<BusinessCalendar>().HasQueryFilter(c => c.OrganizationId == null || c.OrganizationId == CurrentTenant.OrganizationId);
        modelBuilder.Entity<SlaPolicy>().HasQueryFilter(s => s.OrganizationId == null || s.OrganizationId == CurrentTenant.OrganizationId);
        modelBuilder.Entity<BusinessCalendarPeriod>().HasQueryFilter(p => BusinessCalendars.Any(c => c.Id == p.CalendarId));
        modelBuilder.Entity<BusinessCalendarHoliday>().HasQueryFilter(h => BusinessCalendars.Any(c => c.Id == h.CalendarId));
    }
}
