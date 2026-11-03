namespace NWFM.Shared.Messaging;

/// <summary>
/// Sends outbound email. The transport is fixed by the <c>Email</c> settings section; callers
/// supply only recipients and content.
/// </summary>
/// <remarks>
/// Body is always treated as HTML (UTF-8). When <c>UseMock</c> is true the message is written under
/// <c>MockFolder</c> instead of SMTP. When the channel is disabled and mock is off the call
/// returns without sending and without throwing.
/// </remarks>
public interface IEmailService
{
    /// <summary>
    /// Sends one message. Recipients resolve as: <paramref name="toAsList"/> wins when it holds any
    /// non-empty address, otherwise <paramref name="to"/> is the single recipient.
    /// </summary>
    /// <param name="to">Single recipient, used when <paramref name="toAsList"/> is null or empty.</param>
    /// <param name="subject">Message subject.</param>
    /// <param name="body">HTML body.</param>
    /// <param name="senderName">From display name; falls back to the configured <c>SenderName</c>.</param>
    /// <param name="cc">Optional carbon-copy recipients.</param>
    /// <param name="toAsList">Multiple recipients; takes precedence over <paramref name="to"/>.</param>
    /// <param name="cancellationToken">Cancels the send.</param>
    Task SendAsync(
        string to,
        string subject,
        string body,
        string? senderName = null,
        IReadOnlyList<string>? cc = null,
        IReadOnlyList<string>? toAsList = null,
        CancellationToken cancellationToken = default);
}
