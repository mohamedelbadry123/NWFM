using System.Text.Json;
using Auth.Infrastructure.Operations;

// Connection secrets are read from the process environment, never command-line
// arguments, generated output or audit records.
try
{
    if (args.Length < 2 || !Guid.TryParse(args[1], out var tenantId) || tenantId == Guid.Empty)
        throw new ArgumentException("Usage: offboard <tenant-guid> <reason> [--legal-hold] | retention <tenant-guid> | verify-restore <tenant-guid> <storage-root> <backup-reference> <reason>");
    var connection = Environment.GetEnvironmentVariable("NWFM_TENANT_ADMIN_CONNECTION")
        ?? throw new InvalidOperationException("NWFM_TENANT_ADMIN_CONNECTION is required.");
    var retentionOptions = new TenantRetentionOptions
    {
        ActiveDataDays = RetentionDays("NWFM_RETENTION_ACTIVE_DAYS", 30),
        BackupDays = RetentionDays("NWFM_RETENTION_BACKUP_DAYS", 90),
        AuditDays = RetentionDays("NWFM_RETENTION_AUDIT_DAYS", 365)
    };
    retentionOptions.Validate();
    var operations = new TenantLifecycleOperations(connection, TimeProvider.System, retentionOptions);
    switch (args[0])
    {
        case "offboard" when args.Length is 3 or 4:
            if (args.Length == 4 && args[3] != "--legal-hold")
                throw new ArgumentException("The optional offboarding flag is --legal-hold.");
            var schedule = await operations.OffboardAsync(tenantId, args[2], args.Length == 4);
            Console.WriteLine(JsonSerializer.Serialize(schedule));
            break;
        case "retention" when args.Length == 2:
            var retention = await operations.RetentionAsync(tenantId);
            Console.WriteLine(JsonSerializer.Serialize(new { Schedule = retention, Status = retention.Status(DateTimeOffset.UtcNow) }));
            break;
        case "verify-restore" when args.Length == 5:
            var recovery = Environment.GetEnvironmentVariable("NWFM_TENANT_RECOVERY_CONNECTION")
                ?? throw new InvalidOperationException("NWFM_TENANT_RECOVERY_CONNECTION is required.");
            await operations.VerifyRestoreAsync(tenantId, recovery, args[2], args[3], args[4]);
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                TenantId = tenantId,
                OwnershipVerified = true,
                NextStep = "Review a data-only recovery against current access, legal hold and retention rules. No data has been copied."
            }));
            break;
        default:
            throw new ArgumentException("Invalid operation or arguments.");
    }
    return 0;
}
catch (Exception error) when (error is ArgumentException or InvalidOperationException)
{
    Console.Error.WriteLine(error.Message);
    return 1;
}
catch (Exception)
{
    Console.Error.WriteLine("Tenant operation failed. Check restricted database diagnostics; no connection secrets are printed.");
    return 1;
}

static int RetentionDays(string name, int defaultValue)
{
    var value = Environment.GetEnvironmentVariable(name);
    if (value is null) return defaultValue;
    return int.TryParse(value, System.Globalization.NumberStyles.None,
        System.Globalization.CultureInfo.InvariantCulture, out var days)
        ? days : throw new ArgumentException($"{name} must be a positive integer.");
}
