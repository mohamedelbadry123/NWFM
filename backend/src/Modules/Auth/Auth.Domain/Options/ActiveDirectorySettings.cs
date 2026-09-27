namespace Auth.Domain.Options;

public sealed class ActiveDirectorySettings
{
    public const string SectionName = "ActiveDirectory";
    public const int DefaultLdapPort = 389;
    public const int DefaultLdapsPort = 636;

    public bool Enabled { get; set; }
    public string Domain { get; set; } = string.Empty;
    public string Server { get; set; } = string.Empty;
    public int Port { get; set; }
    public bool UseSsl { get; set; } = true;
    public bool UseStartTls { get; set; }
    public bool TrustServerCertificate { get; set; }
    public ActiveDirectoryUserNameFormat UserNameFormat { get; set; } = ActiveDirectoryUserNameFormat.UserPrincipalName;
    public string NetBiosName { get; set; } = string.Empty;
    public string UserPrincipalNameSuffix { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 15;
}

public enum ActiveDirectoryUserNameFormat
{
    UserPrincipalName,
    DownLevel,
    AsTyped
}
