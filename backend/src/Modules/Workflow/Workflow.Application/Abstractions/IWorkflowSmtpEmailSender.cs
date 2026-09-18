namespace Workflow.Application.Abstractions;

/// <summary>
/// Sends a plain-text/HTML email for a workflow notification.
/// Implemented in Workflow.Infrastructure using System.Net.Mail.SmtpClient.
/// When SMTP is not configured, the implementation no-ops silently.
/// </summary>
public interface IWorkflowSmtpEmailSender
{
    /// <summary>
    /// Attempts to send an email to each address in <paramref name="toAddresses"/>.
    /// Returns true when at least one message was dispatched without error.
    /// Logs and swallows SMTP exceptions so a mail failure never aborts the workflow.
    /// </summary>
    Task<bool> TrySendAsync(
        IReadOnlyList<string> toAddresses,
        string subject,
        string body,
        CancellationToken cancellationToken = default);
}
