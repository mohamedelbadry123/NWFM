namespace Auth.Infrastructure.Operations;

public sealed record TenantRetentionSchedule(
    Guid TenantId,
    DateTimeOffset OffboardedAt,
    DateTimeOffset ActiveDataDue,
    DateTimeOffset BackupsDue,
    DateTimeOffset AuditDue,
    bool LegalHold)
{
    public string Status(DateTimeOffset now) => LegalHold ? "OnHold"
        : now >= ActiveDataDue ? "ActionRequired" : "Pending";
}

public sealed class TenantRetentionOptions
{
    public int ActiveDataDays { get; init; } = 30;
    public int BackupDays { get; init; } = 90;
    public int AuditDays { get; init; } = 365;

    public void Validate()
    {
        if (ActiveDataDays is < 1 or > 36500 || BackupDays is < 1 or > 36500 ||
            AuditDays is < 1 or > 36500 || BackupDays < ActiveDataDays || AuditDays < ActiveDataDays)
            throw new ArgumentException("Retention must be 1–36500 days, with backup and audit retention at least as long as active-data retention.");
    }
}

/// <summary>Approved defaults; legal holds restrict deletion rather than grant access.</summary>
public static class TenantRetentionPolicy
{
    public static TenantRetentionSchedule Schedule(Guid tenantId, DateTimeOffset now, bool legalHold,
        TenantRetentionOptions? options = null)
    {
        if (tenantId == Guid.Empty) throw new ArgumentException("A tenant ID is required.", nameof(tenantId));
        options ??= new TenantRetentionOptions();
        options.Validate();
        return new(tenantId, now.ToUniversalTime(), now.AddDays(options.ActiveDataDays).ToUniversalTime(),
            now.AddDays(options.BackupDays).ToUniversalTime(), now.AddDays(options.AuditDays).ToUniversalTime(), legalHold);
    }

    public static void RequireReason(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 1024)
            throw new ArgumentException("An operator reason between 1 and 1024 characters is required.", nameof(reason));
    }

    public static void RequireRecoveryDatabase(string database, bool readOnly, bool singleUser)
    {
        if (!database.StartsWith("NWFM_RECOVERY_", StringComparison.OrdinalIgnoreCase) ||
            database.Length <= "NWFM_RECOVERY_".Length || !readOnly || !singleUser)
            throw new InvalidOperationException("Recovery verification requires a separately restored NWFM_RECOVERY_ database in READ_ONLY and SINGLE_USER mode.");
    }

    public static void RequireStorageOwner(Guid expectedTenant, string marker)
    {
        if (expectedTenant == Guid.Empty || !Guid.TryParseExact(marker, "D", out var actualTenant) ||
            actualTenant != expectedTenant)
            throw new InvalidOperationException("The recovery storage root does not belong to the target tenant.");
    }
}
