namespace Workflow.Infrastructure.Services;

using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NWFM.Shared.Integration.Workflow;
using Workflow.Application.Abstractions;
using Workflow.Application.Settings;
using Workflow.Domain.Entities;
using Workflow.Domain.Enums;
using Workflow.Domain.Repositories;

/// <summary>
/// Persists in-app notifications. External email requires the durable integration
/// worker and a configured SMTP connection; legacy email calls fail explicitly.
/// </summary>
internal sealed class AuditingWorkflowNotificationPublisher : IWorkflowNotificationPublisher
{
    private readonly IWorkflowNotificationLogRepository _logRepo;
    private readonly IWorkflowSmtpEmailSender _smtp;
    private readonly IOptions<WorkflowSettings> _settings;
    private readonly ILogger<AuditingWorkflowNotificationPublisher> _logger;

    public AuditingWorkflowNotificationPublisher(
        IWorkflowNotificationLogRepository logRepo,
        IWorkflowSmtpEmailSender smtp,
        IOptions<WorkflowSettings> settings,
        ILogger<AuditingWorkflowNotificationPublisher> logger)
    {
        _logRepo  = logRepo;
        _smtp     = smtp;
        _settings = settings;
        _logger   = logger;
    }

    public async Task PublishAsync(WorkflowNotificationRequest request, CancellationToken cancellationToken = default)
    {
        var recipientsJson = JsonSerializer.Serialize(request.RecipientUserIds);
        var variablesJson  = JsonSerializer.Serialize(request.Variables);
        var channels       = request.Channels.ToString();

        var wantsEmail     = request.Channels.HasFlag(WorkflowNotificationChannel.Email);
        var wantsInApp     = request.Channels.HasFlag(WorkflowNotificationChannel.InApp);
        var smtpConfigured = !string.IsNullOrWhiteSpace(_settings.Value.Smtp?.Host);

        _logger.LogInformation(
            "Workflow notification processing. TemplateKey={TemplateKey} Channels={Channels} Recipients={RecipientCount} OrganizationId={OrganizationId} CorrelationId={CorrelationId} SmtpConfigured={SmtpConfigured}",
            request.TemplateKey,
            channels,
            request.RecipientUserIds.Count,
            request.OrganizationId,
            request.CorrelationId,
            smtpConfigured);

        // External email is delivered only by the durable integration worker. A
        // legacy publisher call must never send diagnostic mail to the sender.
        if (wantsInApp)
            await _logRepo.AddAsync(WorkflowNotificationLog.Create(request.OrganizationId, request.TemplateKey, "InApp",
                recipientsJson, variablesJson, DateTime.UtcNow, request.CorrelationId, WorkflowNotificationLogStatus.Delivered), cancellationToken);
        if (wantsEmail)
        {
            await _logRepo.AddAsync(WorkflowNotificationLog.Create(request.OrganizationId, request.TemplateKey, "Email",
                recipientsJson, "{}", DateTime.UtcNow, request.CorrelationId, WorkflowNotificationLogStatus.Failed), cancellationToken);
            throw new InvalidOperationException("Email delivery requires a Notification activity with a configured SMTP connection.");
        }
    }
    internal static WorkflowNotificationLogStatus ResolveStatus(
        bool wantsEmail,
        bool wantsInApp,
        bool smtpConfigured,
        bool emailDelivered = false)
    {
        // In-app is delivered via the durable log (UI can read notification logs).
        if (wantsInApp && !wantsEmail)
            return WorkflowNotificationLogStatus.Delivered;

        if (wantsInApp && wantsEmail)
        {
            if (!smtpConfigured) return WorkflowNotificationLogStatus.Failed;
            return emailDelivered
                ? WorkflowNotificationLogStatus.Delivered
                : WorkflowNotificationLogStatus.Failed;
        }

        if (wantsEmail)
            return emailDelivered
                ? WorkflowNotificationLogStatus.Delivered
                : WorkflowNotificationLogStatus.Failed;

        return WorkflowNotificationLogStatus.Logged;
    }
}
