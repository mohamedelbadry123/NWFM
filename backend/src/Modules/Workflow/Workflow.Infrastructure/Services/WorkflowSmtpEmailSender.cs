namespace Workflow.Infrastructure.Services;

using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Workflow.Application.Abstractions;
using Workflow.Application.Settings;

/// <summary>
/// Sends workflow notification emails via System.Net.Mail.SmtpClient.
/// Requires <see cref="WorkflowSmtpSettings.Host"/> to be set; no-ops otherwise.
/// Errors are caught and logged so a mail failure never surfaces to the caller.
/// </summary>
internal sealed class WorkflowSmtpEmailSender : IWorkflowSmtpEmailSender
{
    private readonly WorkflowSettings _settings;
    private readonly ILogger<WorkflowSmtpEmailSender> _logger;

    public WorkflowSmtpEmailSender(
        IOptions<WorkflowSettings> settings,
        ILogger<WorkflowSmtpEmailSender> logger)
    {
        _settings = settings.Value;
        _logger   = logger;
    }

    public async Task<bool> TrySendAsync(
        IReadOnlyList<string> toAddresses,
        string subject,
        string body,
        CancellationToken cancellationToken = default)
    {
        var smtp = _settings.Smtp;
        if (smtp is null || string.IsNullOrWhiteSpace(smtp.Host))
        {
            _logger.LogDebug("SMTP not configured — skipping email send. Subject={Subject}", subject);
            return false;
        }

        if (toAddresses.Count == 0)
        {
            _logger.LogInformation(
                "No recipient email addresses resolved — marking notification as Queued. Subject={Subject}", subject);
            return false;
        }

        try
        {
            using var client = BuildClient(smtp);
            using var message = BuildMessage(smtp.FromAddress ?? "noreply@privora.sa", toAddresses, subject, body);

            await client.SendMailAsync(message, cancellationToken);

            _logger.LogInformation(
                "Workflow SMTP email dispatched. Subject={Subject} Recipients={Count} Host={Host}",
                subject, toAddresses.Count, smtp.Host);

            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Workflow SMTP send failed. Subject={Subject} Host={Host}", subject, smtp.Host);
            return false;
        }
    }

    private static SmtpClient BuildClient(WorkflowSmtpSettings smtp)
    {
        var client = new SmtpClient(smtp.Host, smtp.Port)
        {
            EnableSsl   = smtp.UseSsl,
            DeliveryMethod = SmtpDeliveryMethod.Network,
            UseDefaultCredentials = false,
        };
        return client;
    }

    private static MailMessage BuildMessage(
        string from,
        IReadOnlyList<string> toAddresses,
        string subject,
        string body)
    {
        var msg = new MailMessage { From = new MailAddress(from), Subject = subject, Body = body };
        foreach (var address in toAddresses)
            msg.To.Add(address);
        return msg;
    }
}
