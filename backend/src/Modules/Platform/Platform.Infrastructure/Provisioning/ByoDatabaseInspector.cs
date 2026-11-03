using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NWFM.Shared.Options;
using Platform.Application.Common.Interfaces;

namespace Platform.Infrastructure.Provisioning;

/// <summary>
/// Checks a customer's SQL Server before a tenant is put on it. The connection test is reachable
/// by anyone who has confirmed an e-mail address, so it is built as an SSRF surface first: separate
/// fields only, the host resolved and screened before any packet is sent, and the connection made to
/// the screened address itself so DNS cannot be re-pointed between the check and the connect.
/// </summary>
internal sealed partial class ByoDatabaseInspector(IOptions<PlatformOptions> platformOptions, ILogger<ByoDatabaseInspector> logger)
    : IByoDatabaseInspector
{
    public const int DefaultPort = 1433;
    private const int MinimumSqlServerMajorVersion = 13;
    private const int ConnectTimeoutSeconds = 10;
    private const int CommandTimeoutSeconds = 15;
    private const string ApplicationName = "NWFM";

    private const string VersionSql = "SELECT CAST(SERVERPROPERTY('ProductMajorVersion') AS int), CAST(SERVERPROPERTY('EngineEdition') AS int)";

    private const string PermissionsSql = """
        SELECT CASE WHEN IS_ROLEMEMBER('db_owner') = 1 THEN 1
                    WHEN HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'CREATE TABLE') = 1
                     AND HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'CREATE VIEW') = 1
                     AND HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'CREATE PROCEDURE') = 1
                     AND HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'CREATE FUNCTION') = 1
                     AND HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'ALTER') = 1
                     AND HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'REFERENCES') = 1
                     AND HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'SELECT') = 1
                     AND HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'INSERT') = 1
                     AND HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'UPDATE') = 1
                     AND HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'DELETE') = 1
                     AND HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'EXECUTE') = 1 THEN 1
                    ELSE 0 END
        """;

    // User tables, and whether one of them is the tenant boundary every NWFM tenant database carries.
    // Which tenant it is bound to is enforced when the boundary is applied (a foreign binding is refused).
    private const string SchemaSql = """
        SELECT (SELECT COUNT(*) FROM sys.tables WHERE is_ms_shipped = 0),
               CASE WHEN OBJECT_ID(N'[Tenancy].[DeploymentBoundary]') IS NULL THEN 0 ELSE 1 END
        """;

    // The runtime principal must hold no database role and no server role; its grants come from migration.
    private const string RuntimeSql = """
        SELECT CASE WHEN EXISTS (SELECT 1 FROM sys.database_role_members WHERE member_principal_id = USER_ID())
                      OR COALESCE(IS_SRVROLEMEMBER(N'sysadmin'), 1) <> 0
                      OR COALESCE(HAS_PERMS_BY_NAME(DB_NAME(), N'DATABASE', N'CONTROL'), 1) <> 0
                    THEN 1 ELSE 0 END, 0
        """;

    public async Task<ByoDatabaseInspection> InspectAsync(ByoDatabaseDetails details, CancellationToken cancellationToken)
    {
        var checks = new List<ByoDatabaseCheck>();

        var fieldError = ValidateFields(details);
        if (fieldError is not null)
        {
            return Fail(checks, ByoDatabaseCheckCodes.Host, fieldError);
        }

        var (address, hostError) = await ResolveAllowedAddressAsync(details.Server.Trim(), cancellationToken);
        if (address is null)
        {
            return Fail(checks, ByoDatabaseCheckCodes.Host, hostError!);
        }

        checks.Add(new ByoDatabaseCheck(ByoDatabaseCheckCodes.Host, true, $"'{details.Server.Trim()}' resolves to a public address."));

        await using var connection = new SqlConnection(BuildTestConnectionString(details, address));

        try
        {
            await connection.OpenAsync(cancellationToken);
        }
        catch (SqlException ex)
        {
            logger.LogInformation("Bring-your-own database test to {Server} failed: {Error}", details.Server, ex.Message);
            return Fail(checks, ByoDatabaseCheckCodes.Connect, DescribeConnectFailure(ex));
        }
        catch (Exception ex) when (ex is InvalidOperationException or SocketException or TimeoutException)
        {
            return Fail(checks, ByoDatabaseCheckCodes.Connect, "Could not connect to the server. Check the host, port and firewall rules.");
        }

        checks.Add(new ByoDatabaseCheck(ByoDatabaseCheckCodes.Connect, true, "Connected and signed in."));
        checks.Add(new ByoDatabaseCheck(ByoDatabaseCheckCodes.Encryption, true, "The connection is encrypted."));

        var (majorVersion, _) = await ReadPairAsync(connection, VersionSql, cancellationToken);
        if (majorVersion < MinimumSqlServerMajorVersion)
        {
            return Fail(checks, ByoDatabaseCheckCodes.Version,
                $"SQL Server 2016 (version {MinimumSqlServerMajorVersion}) or later is required; this server reports version {majorVersion}.");
        }

        checks.Add(new ByoDatabaseCheck(ByoDatabaseCheckCodes.Version, true, $"SQL Server version {majorVersion} is supported."));

        var (hasPermissions, _) = await ReadPairAsync(connection, PermissionsSql, cancellationToken);
        if (hasPermissions != 1)
        {
            return Fail(checks, ByoDatabaseCheckCodes.Permissions,
                "The user needs db_owner on the database (or CREATE TABLE/VIEW/PROCEDURE/FUNCTION, ALTER, REFERENCES, SELECT, INSERT, UPDATE, DELETE and EXECUTE). Run the setup script.");
        }

        checks.Add(new ByoDatabaseCheck(ByoDatabaseCheckCodes.Permissions, true, "The user can create and manage the NWFM schema."));

        var schemaCheck = await CheckSchemaAsync(connection, cancellationToken);
        checks.Add(schemaCheck);
        if (!schemaCheck.Passed)
        {
            return new ByoDatabaseInspection(false, checks);
        }

        // The runtime principal: requests never run as the owner, so it must sign in on its own and
        // hold nothing the migration grants do not give it.
        await using var runtime = new SqlConnection(BuildTestConnectionString(details with
        {
            UserName = details.RuntimeUserName,
            Password = details.RuntimePassword
        }, address));
        try
        {
            await runtime.OpenAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is SqlException or InvalidOperationException or SocketException or TimeoutException)
        {
            return Fail(checks, ByoDatabaseCheckCodes.Runtime, "The runtime user could not sign in to the database. Run the setup script and check its password.");
        }

        var (privileged, _) = await ReadPairAsync(runtime, RuntimeSql, cancellationToken);
        if (privileged != 0)
        {
            return Fail(checks, ByoDatabaseCheckCodes.Runtime,
                "The runtime user must not belong to any database role (including db_owner) or hold server roles.");
        }

        checks.Add(new ByoDatabaseCheck(ByoDatabaseCheckCodes.Runtime, true, "The runtime user signs in and holds no roles."));
        return new ByoDatabaseInspection(true, checks);
    }

    public string BuildConnectionString(ByoDatabaseDetails details) => BuildFor(details, runtime: false);

    public string BuildRuntimeConnectionString(ByoDatabaseDetails details) => BuildFor(details, runtime: true);

    /// <summary>Validates the details as entered, then builds the owner's or the runtime principal's connection.</summary>
    private static string BuildFor(ByoDatabaseDetails details, bool runtime)
    {
        if (ValidateFields(details) is { } error)
        {
            throw new InvalidOperationException(error);
        }

        var builder = BaseBuilder(runtime
            ? details with { UserName = details.RuntimeUserName, Password = details.RuntimePassword }
            : details);
        builder.DataSource = $"tcp:{details.Server.Trim()},{details.Port ?? DefaultPort}";
        return builder.ConnectionString;
    }

    public string BuildSetupScript(string database, string ownerUserName, string runtimeUserName)
    {
        var db = QuoteIdentifier(database);
        var owner = QuoteIdentifier(ownerUserName);
        var runtime = QuoteIdentifier(runtimeUserName);
        var ips = platformOptions.Value.EgressIps;

        var script = new StringBuilder();
        script.AppendLine("-- NWFM database setup. Run as a server administrator, then enter the same names in the signup form.");
        script.AppendLine("-- Two principals: the OWNER that NWFM migrates the schema with, and the RUNTIME user requests run as.");
        script.AppendLine("-- The runtime user gets no role here; NWFM grants it exactly what requests need when it migrates.");
        script.AppendLine("-- Replace both <..._PASSWORD> values with passwords of your choice (you will enter them in the form).");
        script.AppendLine(ips.Length > 0
            ? $"-- Allow inbound TCP to the SQL Server port from: {string.Join(", ", ips)}"
            : "-- Allow inbound TCP to the SQL Server port from the NWFM addresses shown on the signup page.");
        script.AppendLine("-- The connection must be encrypted: the server needs a TLS certificate (or tick 'trust server certificate').");
        script.AppendLine("-- Use a dedicated server where possible: the runtime login must not be able to open any other database.");
        script.AppendLine();
        script.AppendLine("USE master;");
        script.AppendLine("GO");
        script.AppendLine($"IF DB_ID(N'{EscapeLiteral(database)}') IS NULL CREATE DATABASE {db};");
        script.AppendLine($"CREATE LOGIN {owner} WITH PASSWORD = N'<OWNER_PASSWORD>', CHECK_POLICY = ON, DEFAULT_DATABASE = {db};");
        script.AppendLine($"CREATE LOGIN {runtime} WITH PASSWORD = N'<RUNTIME_PASSWORD>', CHECK_POLICY = ON, DEFAULT_DATABASE = {db};");
        script.AppendLine("GO");
        script.AppendLine($"USE {db};");
        script.AppendLine("GO");
        script.AppendLine($"CREATE USER {owner} FOR LOGIN {owner};");
        script.AppendLine($"ALTER ROLE db_owner ADD MEMBER {owner};");
        script.AppendLine($"CREATE USER {runtime} FOR LOGIN {runtime};");
        script.AppendLine("GO");

        return script.ToString();
    }

    public static string? ValidateFields(ByoDatabaseDetails details)
    {
        var server = details.Server?.Trim() ?? string.Empty;

        if (server.Length == 0 || server.Length > 253 || Uri.CheckHostName(server) is UriHostNameType.Unknown or UriHostNameType.Basic)
        {
            return "Enter the server as a host name or IP address only — no protocol, instance name, port or extra options.";
        }

        if (details.Port is < 1 or > 65535)
        {
            return "The port must be between 1 and 65535.";
        }

        if (!SqlNamePattern().IsMatch(details.Database ?? string.Empty))
        {
            return "The database name may contain letters, digits, '_' and '-' only (up to 128 characters).";
        }

        if (!SqlNamePattern().IsMatch(details.UserName ?? string.Empty))
        {
            return "The user name may contain letters, digits, '_' and '-' only (up to 128 characters).";
        }

        if (string.IsNullOrEmpty(details.Password) || details.Password.Length > 128)
        {
            return "Enter the database user's password (up to 128 characters).";
        }

        if (!SqlNamePattern().IsMatch(details.RuntimeUserName ?? string.Empty)
            || string.Equals(details.RuntimeUserName, details.UserName, StringComparison.OrdinalIgnoreCase))
        {
            return "The runtime user name may contain letters, digits, '_' and '-' only, and must differ from the owner.";
        }

        if (string.IsNullOrEmpty(details.RuntimePassword) || details.RuntimePassword.Length > 128)
        {
            return "Enter the runtime user's password (up to 128 characters).";
        }

        return null;
    }

    private async Task<(IPAddress? Address, string? Error)> ResolveAllowedAddressAsync(string server, CancellationToken cancellationToken)
    {
        IPAddress[] addresses;

        if (IPAddress.TryParse(server, out var literal))
        {
            addresses = [literal];
        }
        else
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(ConnectTimeoutSeconds));
                addresses = await Dns.GetHostAddressesAsync(server, timeout.Token);
            }
            catch (Exception ex) when (ex is SocketException or OperationCanceledException && !cancellationToken.IsCancellationRequested)
            {
                return (null, $"The host name '{server}' could not be resolved.");
            }
        }

        if (addresses.Length == 0)
        {
            return (null, $"The host name '{server}' could not be resolved.");
        }

        // Every address must pass, not just the first: a name with one public and one private
        // record would otherwise let the driver pick the private one.
        if (!platformOptions.Value.AllowPrivateNetworkDatabaseHosts && addresses.Any(IsNonPublic))
        {
            return (null, "The server must be reachable on a public address. Private, loopback and link-local addresses are not allowed.");
        }

        var chosen = addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork) ?? addresses[0];
        return (chosen, null);
    }

    public static bool IsNonPublic(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any)
            || address.Equals(IPAddress.Broadcast))
        {
            return true;
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var bytes = address.GetAddressBytes();
            return address.IsIPv6LinkLocal
                || address.IsIPv6SiteLocal
                || address.IsIPv6Multicast
                || (bytes[0] & 0xFE) == 0xFC            // fc00::/7 unique local
                || (bytes[0] == 0x00 && bytes[1] == 0x64 && bytes[2] == 0xFF && bytes[3] == 0x9B); // 64:ff9b::/96 NAT64
        }

        var b = address.GetAddressBytes();
        return b[0] == 10
            || b[0] == 0
            || (b[0] == 100 && b[1] >= 64 && b[1] <= 127)  // 100.64.0.0/10 carrier-grade NAT
            || (b[0] == 169 && b[1] == 254)                // link-local, cloud metadata
            || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
            || (b[0] == 192 && b[1] == 168)
            || (b[0] == 192 && b[1] == 0 && b[2] == 0)
            || (b[0] == 198 && (b[1] == 18 || b[1] == 19))  // benchmarking
            || b[0] >= 224;                                 // multicast and reserved
    }

    private static string BuildTestConnectionString(ByoDatabaseDetails details, IPAddress address)
    {
        var builder = BaseBuilder(details);
        var host = address.AddressFamily == AddressFamily.InterNetworkV6 ? $"[{address}]" : address.ToString();

        builder.DataSource = $"tcp:{host},{details.Port ?? DefaultPort}";
        builder.Pooling = false;

        // Connecting to the screened IP keeps certificate validation pointed at the name the customer gave.
        if (!IPAddress.TryParse(details.Server.Trim(), out _))
        {
            builder.HostNameInCertificate = details.Server.Trim();
        }

        return builder.ConnectionString;
    }

    private static SqlConnectionStringBuilder BaseBuilder(ByoDatabaseDetails details) => new()
    {
        InitialCatalog = details.Database.Trim(),
        UserID = details.UserName.Trim(),
        Password = details.Password,
        IntegratedSecurity = false,
        PersistSecurityInfo = false,
        Encrypt = SqlConnectionEncryptOption.Mandatory,
        TrustServerCertificate = details.TrustServerCertificate,
        ConnectTimeout = ConnectTimeoutSeconds,
        ConnectRetryCount = 0,
        ApplicationName = ApplicationName,
        MultiSubnetFailover = false
    };

    private static async Task<ByoDatabaseCheck> CheckSchemaAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        var (tables, conciergeSchema) = await ReadPairAsync(connection, SchemaSql, cancellationToken);

        if (conciergeSchema == 1)
        {
            return new ByoDatabaseCheck(ByoDatabaseCheckCodes.Schema, true, "The database holds a NWFM schema; it will be brought up to date.");
        }

        return tables == 0
            ? new ByoDatabaseCheck(ByoDatabaseCheckCodes.Schema, true, "The database is empty; NWFM will create its schema.")
            : new ByoDatabaseCheck(ByoDatabaseCheckCodes.Schema, false, "The database already contains tables that are not from NWFM. Use an empty database.");
    }

    private static async Task<(int First, int Second)> ReadPairAsync(SqlConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = CommandTimeoutSeconds;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return (0, 0);
        }

        var first = reader.IsDBNull(0) ? 0 : reader.GetInt32(0);
        var second = reader.FieldCount > 1 && !reader.IsDBNull(1) ? reader.GetInt32(1) : 0;
        return (first, second);
    }

    private static string DescribeConnectFailure(SqlException ex) => ex.Number switch
    {
        18456 => "The server refused the user name or password.",
        4060 => "The database does not exist, or the user cannot open it.",
        -2 or 53 or 258 or 10060 or 10061 => "Could not reach the server. Check the host, port and that our addresses are allowed through the firewall.",
        _ when ex.Message.Contains("certificate", StringComparison.OrdinalIgnoreCase) =>
            "The server's TLS certificate is not trusted. Install a trusted certificate or tick 'trust server certificate'.",
        _ when ex.Message.Contains("encrypt", StringComparison.OrdinalIgnoreCase) || ex.Message.Contains("SSL", StringComparison.OrdinalIgnoreCase) =>
            "The server does not accept encrypted connections. Enable TLS on the SQL Server.",
        _ => "Could not connect to the server. Check the details and firewall rules."
    };

    private static ByoDatabaseInspection Fail(List<ByoDatabaseCheck> checks, string code, string message)
    {
        checks.Add(new ByoDatabaseCheck(code, false, message));
        return new ByoDatabaseInspection(false, checks);
    }

    private static string QuoteIdentifier(string name) => "[" + name.Replace("]", "]]", StringComparison.Ordinal) + "]";

    private static string EscapeLiteral(string value) => value.Replace("'", "''", StringComparison.Ordinal);

    [GeneratedRegex("^[A-Za-z0-9_-]{1,128}$", RegexOptions.CultureInvariant)]
    private static partial Regex SqlNamePattern();
}
