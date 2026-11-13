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
        _maximumPendingPerQueue = settings?.Value.MaxPendingPerQueue ?? 10000;
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
    public DbSet<WorkflowFormReceiptReservation> FormReceiptReservations => Set<WorkflowFormReceiptReservation>();
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
    public DbSet<WorkflowMappingInvocation> MappingInvocations => Set<WorkflowMappingInvocation>();
    public DbSet<WorkflowEventSubscription> EventSubscriptions => Set<WorkflowEventSubscription>();
    public DbSet<WorkflowEventReceipt> EventReceipts => Set<WorkflowEventReceipt>();
    public DbSet<WorkflowRoutingScope> RoutingScopes => Set<WorkflowRoutingScope>();
    public DbSet<WorkflowRoutingResult> RoutingResults => Set<WorkflowRoutingResult>();
    public DbSet<WorkflowRoutingActivation> RoutingActivations => Set<WorkflowRoutingActivation>();
    public DbSet<WorkflowRoutingUpgradeSnapshot> RoutingUpgradeSnapshots => Set<WorkflowRoutingUpgradeSnapshot>();

    private Dictionary<Guid, WorkItem> ReceiptReservations()
    {
        var reservations = new Dictionary<Guid, WorkItem>();
        foreach (var entry in ChangeTracker.Entries<WorkItem>().Where(e => e.State is EntityState.Added or EntityState.Modified))
        {
            var item = entry.Entity;
            void Reserve(Guid key)
            {
                if (key == Guid.Empty) return;
                if (reservations.TryGetValue(key, out var owner) && owner.Id != item.Id)
                    throw new InvalidOperationException("A workflow receipt key already has another owner.");
                reservations[key] = item;
            }
            Reserve(item.Id);
            var visitId = item.Id;
            if (item.FormProgressJson is not null)
            {
                var progress = System.Text.Json.JsonSerializer.Deserialize<Workflow.Application.Workspace.ActivityFormProgress>(item.FormProgressJson)!;
                visitId = progress.VisitId;
                Reserve(visitId);
                foreach (var visit in progress.Visits) { Reserve(visit.VisitId); Reserve(visit.ReceiptKey ?? visit.VisitId); }
            }
            if (item.ClaimedByUserId is Guid actor) Reserve(item.FormReceiptKey(visitId, actor));
            if (item.CompletedFormReceiptKey is Guid completed) Reserve(completed);
        }
        foreach (var tracked in FormReceiptReservations.Local)
        {
            if (reservations.TryGetValue(tracked.ReceiptKey, out var item) && item.Id != tracked.WorkItemId)
                throw new InvalidOperationException("A workflow receipt key already has another owner.");
            reservations.Remove(tracked.ReceiptKey);
        }
        return reservations;
    }

    private void AddReservations(Dictionary<Guid, WorkItem> requested, IEnumerable<WorkflowFormReceiptReservation> existing)
    {
        foreach (var row in existing)
        {
            if (requested[row.ReceiptKey].Id != row.WorkItemId)
                throw new InvalidOperationException("A workflow receipt key already has another owner.");
            requested.Remove(row.ReceiptKey);
        }
        FormReceiptReservations.AddRange(requested.Select(r => WorkflowFormReceiptReservation.Create(r.Key, r.Value)));
    }

    private void PrepareReceiptReservations()
    {
        var requested = ReceiptReservations();
        if (requested.Count > 0)
        {
            var keys = requested.Keys.ToArray();
            AddReservations(requested, FormReceiptReservations.IgnoreQueryFilters().Where(r => keys.Contains(r.ReceiptKey)).ToList());
        }

    }

    private async Task PrepareReceiptReservationsAsync(CancellationToken cancellationToken)
    {
        var requested = ReceiptReservations();
        if (requested.Count > 0)
        {
            var keys = requested.Keys.ToArray();
            AddReservations(requested, await FormReceiptReservations.IgnoreQueryFilters().Where(r => keys.Contains(r.ReceiptKey)).ToListAsync(cancellationToken));
        }

    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasDefaultSchema("Workflow");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(WorkflowDbContext).Assembly);
        modelBuilder.Entity<WorkflowFormReceiptReservation>().HasQueryFilter(r => r.OrganizationId == CurrentTenant.OrganizationId);

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
