namespace Workflow.Application.Settings;

public sealed class WorkflowSettings
{
    public const string SectionName = "WorkflowSettings";

    public bool IsEnabled { get; set; }
    public bool AllowPrivateConnections { get; set; }

    /// <summary>
    /// When true, seeds the designer example workflow on startup.
    /// Defaults to false. Must be explicitly enabled per environment.
    /// Never set to true in Production.
    /// </summary>
    public bool SeedDesignerExamples { get; set; }

    /// <summary>
    /// Optional SMTP settings for workflow email outbox acceptance.
    /// When Host is empty, email channels stay Queued; InApp still Delivered.
    /// </summary>
    public WorkflowSmtpSettings? Smtp { get; set; }
}

public sealed class WorkflowSmtpSettings
{
    public string? Host { get; set; }
    public int Port { get; set; } = 587;
    public string? FromAddress { get; set; }
    public bool UseSsl { get; set; } = true;
}
