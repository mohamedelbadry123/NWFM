namespace Workflow.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Workflow.Application.Abstractions;
using Workflow.Application.Settings;
using Workflow.Domain.Repositories;
using Workflow.Infrastructure.Background;
using Workflow.Infrastructure.Persistence;
using Workflow.Infrastructure.Persistence.Repositories;
using Workflow.Infrastructure.Services;
using Workflow.Application.Integrations;

public static class DependencyInjection
{
    public static IServiceCollection AddWorkflowInfrastructure(
        this IServiceCollection services,
        string connectionString,
        IConfiguration configuration)
    {
        services.Configure<WorkflowSettings>(
            configuration.GetSection(WorkflowSettings.SectionName));

        services.AddDbContext<WorkflowDbContext>(options =>
            options.UseSqlServer(
                connectionString,
                sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", "Workflow")));

        services.AddScoped<IWorkflowFeatureGate, WorkflowFeatureGate>();
        services.AddScoped<Workflow.Application.Workspace.IWorkflowWorkspacePublisher, WorkflowWorkspacePublisher>();
        services.AddTransient(typeof(MediatR.IPipelineBehavior<,>), typeof(WorkflowRuntimeCommandLock<,>));
        services.AddScoped<IWorkflowParticipantRepository, WorkflowParticipantRepository>();
        services.AddScoped<IWorkflowAssignmentGroupRepository, WorkflowAssignmentGroupRepository>();
        services.AddScoped<IWorkflowDepartmentRepository, WorkflowDepartmentRepository>();
        services.AddScoped<IWorkflowDefinitionRepository, WorkflowDefinitionRepository>();
        services.AddScoped<IWorkflowVersionRepository, WorkflowVersionRepository>();
        services.AddScoped<IWorkflowBindingRepository, WorkflowBindingRepository>();
        services.AddScoped<IWorkflowBindingAssignmentMappingRepository, WorkflowBindingAssignmentMappingRepository>();
        services.AddScoped<IWorkflowXmlCompiler, WorkflowXmlCompiler>();

        // Runtime repositories
        services.AddScoped<IWorkflowInstanceRepository, WorkflowInstanceRepository>();
        services.AddScoped<IActivityInstanceRepository, ActivityInstanceRepository>();
        services.AddScoped<IWorkItemRepository, WorkItemRepository>();
        services.AddScoped<IWorkItemCandidateRepository, WorkItemCandidateRepository>();
        services.AddScoped<IWorkflowVariableRepository, WorkflowVariableRepository>();
        services.AddScoped<IWorkflowEventRepository, WorkflowEventRepository>();
        services.AddScoped<ITransitionInstanceRepository, TransitionInstanceRepository>();

        // Advanced domain repositories
        services.AddScoped<IWorkflowTimerRepository, WorkflowTimerRepository>();
        services.AddScoped<IWorkflowIncidentRepository, WorkflowIncidentRepository>();
        services.AddScoped<IBusinessCalendarRepository, BusinessCalendarRepository>();
        services.AddScoped<ISlaPolicyRepository, SlaPolicyRepository>();
        services.AddScoped<IWorkflowExecutionTokenRepository, WorkflowExecutionTokenRepository>();
        services.AddScoped<IWorkflowIntegrationOutboxRepository, WorkflowIntegrationOutboxRepository>();
        services.AddScoped<IWorkflowIntegrationInboxRepository, WorkflowIntegrationInboxRepository>();
        services.AddScoped<IWorkflowNotificationLogRepository, WorkflowNotificationLogRepository>();
        services.AddScoped<IWorkflowRequestRepository, WorkflowRequestRepository>();
        services.AddScoped<IWorkflowRequestProjector, WorkflowRequestProjector>();
        services.AddScoped<IWorkItemDtoAssembler, Workflow.Application.Mapping.WorkItemDtoAssembler>();
        services.AddScoped<IWorkflowHistoryBuilder, Workflow.Application.Helpers.WorkflowHistoryBuilder>();

        // Runtime engine services
        services.AddScoped<IWorkflowEventAppender, WorkflowEventAppender>();
        services.AddScoped<IWorkflowTransitionEvaluator, WorkflowTransitionEvaluator>();
        services.AddScoped<IWorkflowVersionResolver, WorkflowVersionResolver>();
        services.AddScoped<IWorkflowBindingResolver, WorkflowBindingResolver>();
        services.AddScoped<IWorkflowAssignmentResolver, WorkflowAssignmentResolver>();
        services.AddScoped<IWorkflowCandidateFactory, WorkflowCandidateFactory>();
        services.AddScoped<IWorkflowInboxWriter, WorkflowInboxWriter>();
        services.AddScoped<WorkflowRuntimeEngine>();
        services.AddScoped<IWorkflowRuntimeEngine, SerializedWorkflowRuntimeEngine>();
        services.AddDataProtection();
        services.AddScoped<WorkflowIntegrations>();
        services.AddScoped<IWorkflowIntegrations>(sp => sp.GetRequiredService<WorkflowIntegrations>());
        services.AddScoped<IWorkflowIntegrationRuntime>(sp => sp.GetRequiredService<WorkflowIntegrations>());
        services.AddScoped<WorkflowIntegrationTransport>();
        services.AddScoped<WorkflowIntegrationProcessor>();
        services.AddScoped<WorkflowActivityEvents>();
        services.AddScoped<WorkflowWorkspaceProcessor>();
        services.AddScoped<Workflow.Application.Workspace.IWorkflowWorkspace, WorkflowWorkspace>();
        services.AddScoped<Workflow.Application.Workspace.IWorkflowGroupDirectory, WorkflowGroupDirectory>();
        services.AddScoped<NWFM.Shared.Integration.Workflow.IWorkflowActionProvider, HttpWorkflowActionProvider>();
        services.AddScoped<NWFM.Shared.Integration.Workflow.IWorkflowActionProvider, WorkflowVariableActionProvider>();
        services.AddScoped<NWFM.Shared.Integration.Workflow.IWorkflowTriggerService, WorkflowTriggerService>();

        // Advanced engine scaffolding
        services.AddScoped<NWFM.Shared.Integration.Workflow.IWorkflowActionRegistry, WorkflowActionRegistry>();
        services.AddScoped<IBusinessCalendarService, BusinessCalendarService>();
        services.AddScoped<IWorkflowTimerService, WorkflowTimerService>();
        services.AddScoped<IWorkflowIncidentService, WorkflowIncidentService>();
        services.AddScoped<IWorkflowOutcomeDispatcher, WorkflowOutcomeDispatcher>();
        services.AddScoped<NWFM.Shared.Integration.Workflow.IWorkflowOutcomePublisher, OutboxWorkflowOutcomePublisher>();
        services.AddScoped<IWorkflowSmtpEmailSender, WorkflowSmtpEmailSender>();
        services.AddScoped<NWFM.Shared.Integration.Workflow.IWorkflowNotificationPublisher, AuditingWorkflowNotificationPublisher>();
        // NullWorkflowNotificationPublisher retained for tests that opt in explicitly.

        services.AddHostedService<WorkflowTimerHostedService>();
        services.AddHostedService<WorkflowOutboxHostedService>();
        services.AddHostedService<WorkflowInboxHostedService>();
        services.AddHostedService<WorkflowIntegrationHostedService>();
        return services;
    }
}
