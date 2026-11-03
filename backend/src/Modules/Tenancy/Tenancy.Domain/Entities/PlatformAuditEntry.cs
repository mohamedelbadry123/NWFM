namespace Tenancy.Domain.Entities;

/// <summary>Append-only record of platform actions: tenant lifecycle, subscription changes, support sessions.</summary>
public sealed class PlatformAuditEntry
{
    public const int ActionMaxLength = 100;
    public const int ActorMaxLength = 256;
    public const int TargetMaxLength = 100;
    public const int DetailsMaxLength = 4000;
    public const int IpMaxLength = 64;

    private PlatformAuditEntry()
    {
    }

    public long Id { get; private set; }

    public DateTime AtUtc { get; private set; }

    public string? ActorId { get; private set; }

    public string? ActorName { get; private set; }

    public string Action { get; private set; } = default!;

    public Guid? TenantId { get; private set; }

    public string? TargetType { get; private set; }

    public string? TargetId { get; private set; }

    public string? Details { get; private set; }

    public string? IpAddress { get; private set; }

    public static PlatformAuditEntry Create(string action, string? actorId, string? actorName, Guid? tenantId,
        string? targetType, string? targetId, string? details, string? ipAddress, DateTime utcNow) => new()
        {
            AtUtc = utcNow,
            Action = action,
            ActorId = actorId,
            ActorName = Truncate(actorName, ActorMaxLength),
            TenantId = tenantId,
            TargetType = targetType,
            TargetId = targetId,
            Details = Truncate(details, DetailsMaxLength),
            IpAddress = Truncate(ipAddress, IpMaxLength)
        };

    private static string? Truncate(string? value, int max) =>
        value is { Length: > 0 } && value.Length > max ? value[..max] : value;
}

/// <summary>Action names written to <see cref="PlatformAuditEntry.Action"/>.</summary>
public static class PlatformAuditActions
{
    public const string TenantCreated = "tenant.created";
    public const string TenantUpdated = "tenant.updated";
    public const string TenantActivated = "tenant.activated";
    public const string TenantSuspended = "tenant.suspended";
    public const string TenantMaintenance = "tenant.maintenance";
    public const string TenantMigrated = "tenant.migrated";
    public const string TenantMigrationFailed = "tenant.migration_failed";
    public const string TenantDatabaseVerified = "tenant.database_verified";
    public const string TenantDatabaseVerificationFailed = "tenant.database_verification_failed";
    public const string TenantOffboarded = "tenant.offboarded";
    public const string SubscriptionChanged = "subscription.changed";
    public const string PlanSaved = "plan.saved";
    public const string DatabaseServerSaved = "database_server.saved";
    public const string PlatformUserSaved = "platform_user.saved";
    public const string PlatformLogin = "platform.login";
    public const string PlatformLoginFailed = "platform.login_failed";
    public const string SignupSubmitted = "signup.submitted";
    public const string SignupCreatedByStaff = "signup.staff_created";
    public const string SignupProvisioned = "signup.provisioned";
    public const string SignupFailed = "signup.failed";
    public const string SignupRejected = "signup.rejected";
    public const string SignupRetried = "signup.retried";
    public const string SignupApproved = "signup.approved";
    public const string ConnectionStringRevealed = "tenant.connection_string_revealed";
    public const string TenantAdminCreated = "tenant_admin.created";
    public const string TenantAdminSuspended = "tenant_admin.suspended";
    public const string TenantAdminReactivated = "tenant_admin.reactivated";
    public const string TenantAdminCredentialsSent = "tenant_admin.credentials_sent";
    public const string CredentialsNotSent = "tenant_admin.credentials_not_sent";
    public const string SupportGranted = "support.granted";
    public const string SupportRevoked = "support.revoked";
    public const string SupportSessionStarted = "support.session_started";
    public const string SupportSessionEnded = "support.session_ended";
    public const string SupportSessionRedeemed = "support.session_redeemed";
}
