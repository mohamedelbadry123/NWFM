namespace Platform.Application.Common.Interfaces;

/// <summary>
/// Connection details of a customer's own SQL Server, as separate fields. A raw connection string is
/// never accepted: it could switch on integrated security, point at a named pipe or a local file, or
/// smuggle in options that turn the connection test into a probe of our network.
/// </summary>
public sealed record ByoDatabaseDetails(
    string Server,
    int? Port,
    string Database,
    string UserName,
    string Password,
    string RuntimeUserName,
    string RuntimePassword,
    bool TrustServerCertificate);

public static class ByoDatabaseCheckCodes
{
    public const string Host = "host";
    public const string Connect = "connect";
    public const string Encryption = "encryption";
    public const string Version = "version";
    public const string Permissions = "permissions";
    public const string Runtime = "runtime";
    public const string Schema = "schema";
}

public sealed record ByoDatabaseCheck(string Code, bool Passed, string Message);

public sealed record ByoDatabaseInspection(bool Succeeded, IReadOnlyList<ByoDatabaseCheck> Checks)
{
    public string Summary => string.Join(" ", Checks.Where(c => !c.Passed).Select(c => c.Message));
}

public interface IByoDatabaseInspector
{
    /// <summary>
    /// Host guard, then connect, encryption, server version, permissions and whether the database is
    /// empty or already holds a compatible NWFM schema, then that the runtime principal connects and
    /// holds no database role. Stops at the first check that rules out the rest.
    /// </summary>
    Task<ByoDatabaseInspection> InspectAsync(ByoDatabaseDetails details, CancellationToken cancellationToken);

    /// <summary>The owner connection string the control plane migrates with. Call only after a successful inspection.</summary>
    string BuildConnectionString(ByoDatabaseDetails details);

    /// <summary>The restricted runtime principal's connection string the tenant API serves requests with.</summary>
    string BuildRuntimeConnectionString(ByoDatabaseDetails details);

    /// <summary>T-SQL the customer runs to create the owner and runtime logins and users NWFM needs.</summary>
    string BuildSetupScript(string database, string ownerUserName, string runtimeUserName);
}
