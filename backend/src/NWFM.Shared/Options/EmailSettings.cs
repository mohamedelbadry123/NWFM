namespace NWFM.Shared.Options;

/// <summary>
/// Outbound email (SMTP). Section <c>Email</c>. Secrets (<see cref="EmailFromPassword"/>) must come
/// from user secrets or environment variables — never commit real values.
/// </summary>
public sealed class EmailSettings
{
    public const string SectionName = "Email";

    public const string DefaultMockFolder = "logs/emails";

    /// <summary>Master email switch. When false and <see cref="UseMock"/> is off, a send returns without sending.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// When true, every send is written as a text file under <see cref="MockFolder"/> instead of SMTP.
    /// Signup and activation links are in those files.
    /// </summary>
    public bool UseMock { get; set; }

    /// <summary>Folder for mock .txt messages. Relative paths are under the API content root.</summary>
    public string MockFolder { get; set; } = DefaultMockFolder;

    public string? Smtp { get; set; }

    public int Port { get; set; } = 587;

    /// <summary>From address and SMTP username.</summary>
    public string? EmailFrom { get; set; }

    public string? EmailFromPassword { get; set; }

    public bool EnableSsl { get; set; } = true;

    /// <summary>Default From display name when the caller does not pass one.</summary>
    public string SenderName { get; set; } = "NWFM";
}
