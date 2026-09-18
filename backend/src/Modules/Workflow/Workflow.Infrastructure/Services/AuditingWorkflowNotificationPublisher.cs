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
/// Persists a durable notification outbox row, marks InApp as delivered, and attempts
/// SMTP delivery when Email channel + Smtp.Host are both configured.
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

        var emailDelivered = false;
        if (wantsEmail && smtpConfigured)
        {
            // No user-email resolver is wired yet — log and queue for ops to drain.
            // When an IUserEmailLookup service is available, resolve addresses here.
            _logger.LogInformation(
                "Workflow email SMTP send attempted. TemplateKey={TemplateKey} Host={Host}",
                request.TemplateKey,
                _settings.Value.Smtp!.Host);

            var subject = $"Workflow Notification: {request.TemplateKey}";
            var body    = $"Correlation: {request.CorrelationId}\n{variablesJson}";

            // Without real email addresses for recipient user ids, send diagnostic to FromAddress.
            var fromAddress = _settings.Value.Smtp.FromAddress;
            var toAddresses = string.IsNullOrWhiteSpace(fromAddress)
                ? []
                : new List<string> { fromAddress };

            emailDelivered = await _smtp.TrySendAsync(toAddresses, subject, body, cancellationToken);

            if (!emailDelivered)
                _logger.LogInformation(
                    "No recipient emails resolved or SMTP failed — notification stays Queued. TemplateKey={TemplateKey}",
                    request.TemplateKey);
        }

        var status = ResolveStatus(wantsEmail, wantsInApp, smtpConfigured, emailDelivered);

        _logger.LogInformation(
            "Workflow notification persisted. Status={Status} TemplateKey={TemplateKey}",
            status, request.TemplateKey);

        var log = WorkflowNotificationLog.Create(
            request.OrganizationId,
            request.TemplateKey,
            channels,
            recipientsJson,
            variablesJson,
            DateTime.UtcNow,
            request.CorrelationId,
            status);

        await _logRepo.AddAsync(log, cancellationToken);
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
            if (!smtpConfigured) return WorkflowNotificationLogStatus.Delivered;
            return emailDelivered
                ? WorkflowNotificationLogStatus.Delivered
                : WorkflowNotificationLogStatus.Queued;
        }

        if (wantsEmail)
            return emailDelivered
                ? WorkflowNotificationLogStatus.Delivered
                : WorkflowNotificationLogStatus.Queued;

        return WorkflowNotificationLogStatus.Logged;
    }
}
