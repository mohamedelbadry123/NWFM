using System.Net;
using System.Net.Mail;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NWFM.Shared.Messaging;
using NWFM.Shared.Options;

namespace Platform.Infrastructure.Messaging;

/// <summary>
/// Sends email over SMTP using <c>System.Net.Mail</c>, configured from
/// <c>Email</c>. When <c>UseMock</c> is true the message is
/// written under <c>MockFolder</c> instead of going to the network.
/// </summary>
/// <remarks>
/// Body is HTML, UTF-8. A disabled channel short-circuits silently unless mock is on. SMTP failures
/// are logged and rethrown so a caller that cares can react; callers that treat mail as best-effort
/// should catch.
/// </remarks>
public sealed class SmtpEmailService(
    IOptions<EmailSettings> options,
    IHostEnvironment environment,
    TimeProvider timeProvider,
    ILogger<SmtpEmailService> logger)
    : IEmailService
{
    private static readonly Regex HrefPattern = new("href=\"([^\"]+)\"", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly EmailSettings _settings = options.Value;

    public async Task SendAsync(
        string to,
        string subject,
        string body,
        string? senderName = null,
        IReadOnlyList<string>? cc = null,
        IReadOnlyList<string>? toAsList = null,
        CancellationToken cancellationToken = default)
    {
        List<string> recipients = ResolveRecipients(to, toAsList);
        if (recipients.Count == 0)
        {
            throw new InvalidOperationException("Cannot send email: no recipients were provided.");
        }

        if (_settings.UseMock)
        {
            await WriteMockAsync(recipients, subject, body, senderName, cc, cancellationToken);
            return;
        }

        if (!_settings.Enabled)
        {
            logger.LogInformation("Email channel disabled; skipping send of '{Subject}'.", subject);
            return;
        }

        if (string.IsNullOrWhiteSpace(_settings.EmailFrom))
        {
            throw new InvalidOperationException(
                "Cannot send email: 'Email:EmailFrom' is not configured.");
        }

        if (string.IsNullOrWhiteSpace(_settings.Smtp))
        {
            throw new InvalidOperationException(
                "Cannot send email: 'Email:Smtp' is not configured.");
        }

        using var message = new MailMessage
        {
            From = new MailAddress(_settings.EmailFrom, senderName ?? _settings.SenderName),
            Subject = subject,
            Body = body,
            IsBodyHtml = true,
            BodyEncoding = Encoding.UTF8,
            SubjectEncoding = Encoding.UTF8,
        };

        foreach (string recipient in recipients)
        {
            message.To.Add(recipient);
        }

        foreach (string copy in NonEmpty(cc))
        {
            message.CC.Add(copy);
        }

        using var client = new SmtpClient(_settings.Smtp, _settings.Port)
        {
            EnableSsl = _settings.EnableSsl,
            UseDefaultCredentials = false,
            DeliveryMethod = SmtpDeliveryMethod.Network,
            Credentials = new NetworkCredential(_settings.EmailFrom, _settings.EmailFromPassword),
        };

        logger.LogInformation(
            "Sending email '{Subject}' to {RecipientCount} recipient(s).",
            subject,
            recipients.Count);

        try
        {
            await client.SendMailAsync(message, cancellationToken);

            logger.LogInformation("Sent email '{Subject}'.", subject);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send email '{Subject}'.", subject);
            throw;
        }
    }

    private async Task WriteMockAsync(
        IReadOnlyList<string> recipients,
        string subject,
        string body,
        string? senderName,
        IReadOnlyList<string>? cc,
        CancellationToken cancellationToken)
    {
        var folder = string.IsNullOrWhiteSpace(_settings.MockFolder)
            ? EmailSettings.DefaultMockFolder
            : _settings.MockFolder.Trim();

        var root = Path.IsPathRooted(folder)
            ? folder
            : Path.Combine(environment.ContentRootPath, folder);

        Directory.CreateDirectory(root);

        var stamp = timeProvider.GetUtcNow().UtcDateTime.ToString("yyyyMMdd-HHmmss");
        var fileName = $"{stamp}-{SanitizeFileName(subject)}-{Guid.NewGuid().ToString("N")[..8]}.txt";
        var path = Path.Combine(root, fileName);

        var from = string.IsNullOrWhiteSpace(_settings.EmailFrom)
            ? (senderName ?? _settings.SenderName ?? "NWFM")
            : $"{senderName ?? _settings.SenderName} <{_settings.EmailFrom}>";

        var copies = NonEmpty(cc);
        var builder = new StringBuilder();
        builder.AppendLine($"From: {from}");
        builder.AppendLine($"To: {string.Join(", ", recipients)}");
        if (copies.Count > 0)
        {
            builder.AppendLine($"Cc: {string.Join(", ", copies)}");
        }

        builder.AppendLine($"Subject: {subject}");
        builder.AppendLine($"SentAtUtc: {timeProvider.GetUtcNow().UtcDateTime:O}");
        builder.AppendLine();

        // The body is HTML, so its links carry '&amp;'. Copied from here into a browser, that turns
        // the second query parameter into 'amp;token' — list each link decoded, ready to paste.
        var links = HrefPattern.Matches(body).Select(m => WebUtility.HtmlDecode(m.Groups[1].Value)).Distinct().ToList();
        if (links.Count > 0)
        {
            builder.AppendLine("Links:");
            foreach (var link in links)
            {
                builder.AppendLine(link);
            }

            builder.AppendLine();
        }

        builder.Append(body);

        await File.WriteAllTextAsync(path, builder.ToString(), Encoding.UTF8, cancellationToken);
        logger.LogInformation("Email '{Subject}' written to {Path} (UseMock).", subject, path);
    }

    private static string SanitizeFileName(string subject)
    {
        if (string.IsNullOrWhiteSpace(subject))
        {
            return "email";
        }

        var invalid = Path.GetInvalidFileNameChars();
        var chars = subject.Trim().Select(ch => invalid.Contains(ch) ? '-' : ch).ToArray();
        var slug = new string(chars).Trim('-');
        return slug.Length == 0 ? "email" : slug.Length > 60 ? slug[..60] : slug;
    }

    private static List<string> ResolveRecipients(string to, IReadOnlyList<string>? toAsList)
    {
        List<string> fromList = NonEmpty(toAsList);
        if (fromList.Count > 0)
        {
            return fromList;
        }

        return string.IsNullOrWhiteSpace(to)
            ? []
            : [to.Trim()];
    }

    private static List<string> NonEmpty(IReadOnlyList<string>? values)
    {
        if (values is null)
        {
            return [];
        }

        return values
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim())
            .ToList();
    }
}
