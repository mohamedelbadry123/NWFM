using Auth.Infrastructure.Operations;

namespace NWFM.Tests.Security;

public sealed class TenantLifecyclePolicyTests
{
    [Fact]
    public void ApprovedRetentionDatesAreFixedFromOffboardingTime()
    {
        var now = new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.FromHours(3));
        var schedule = TenantRetentionPolicy.Schedule(Guid.NewGuid(), now, false);
        Assert.Equal(now.AddDays(30), schedule.ActiveDataDue);
        Assert.Equal(now.AddDays(90), schedule.BackupsDue);
        Assert.Equal(now.AddDays(365), schedule.AuditDue);
        Assert.Equal("Pending", schedule.Status(now.AddDays(29)));
        Assert.Equal("ActionRequired", schedule.Status(now.AddDays(30)));
        Assert.Equal(TimeSpan.Zero, schedule.ActiveDataDue.Offset);
    }

    [Fact]
    public void LegalHoldNeverMakesRetentionDeletionEligible()
    {
        var now = DateTimeOffset.UtcNow;
        var schedule = TenantRetentionPolicy.Schedule(Guid.NewGuid(), now, true);
        Assert.Equal("OnHold", schedule.Status(now.AddYears(10)));
    }

    [Fact]
    public void ExplicitOperatorPolicyChangesTheRecordedDeadlines()
    {
        var now = DateTimeOffset.UtcNow;
        var schedule = TenantRetentionPolicy.Schedule(Guid.NewGuid(), now, false,
            new TenantRetentionOptions { ActiveDataDays = 14, BackupDays = 60, AuditDays = 730 });
        Assert.Equal(now.AddDays(14), schedule.ActiveDataDue);
        Assert.Equal(now.AddDays(60), schedule.BackupsDue);
        Assert.Equal(now.AddDays(730), schedule.AuditDue);
    }

    [Theory]
    [InlineData(0, 90, 365)]
    [InlineData(30, 29, 365)]
    [InlineData(30, 90, 29)]
    [InlineData(30, 36501, 365)]
    public void InvalidRetentionPoliciesCannotBeRecorded(int active, int backup, int audit) =>
        Assert.Throws<ArgumentException>(() => TenantRetentionPolicy.Schedule(Guid.NewGuid(), DateTimeOffset.UtcNow, false,
            new TenantRetentionOptions { ActiveDataDays = active, BackupDays = backup, AuditDays = audit }));

    [Theory]
    [InlineData("production", true, true)]
    [InlineData("NWFM_RECOVERY_", true, true)]
    [InlineData("NWFM_RECOVERY_tenant", false, true)]
    [InlineData("NWFM_RECOVERY_tenant", true, false)]
    public void LiveWritableOrSharedRecoverySourcesAreRejected(string database, bool readOnly, bool singleUser) =>
        Assert.Throws<InvalidOperationException>(() => TenantRetentionPolicy.RequireRecoveryDatabase(database, readOnly, singleUser));

    [Fact]
    public void IsolatedReadOnlyRecoveryIsAccepted() =>
        TenantRetentionPolicy.RequireRecoveryDatabase("NWFM_RECOVERY_tenant", true, true);

    [Fact]
    public void ForeignOrUnboundRecoveryStorageIsRejected()
    {
        var tenant = Guid.NewGuid();
        TenantRetentionPolicy.RequireStorageOwner(tenant, tenant.ToString("D"));
        Assert.Throws<InvalidOperationException>(() => TenantRetentionPolicy.RequireStorageOwner(tenant, Guid.NewGuid().ToString("D")));
        Assert.Throws<InvalidOperationException>(() => TenantRetentionPolicy.RequireStorageOwner(tenant, "malformed"));
        Assert.Throws<InvalidOperationException>(() => TenantRetentionPolicy.RequireStorageOwner(Guid.Empty, Guid.Empty.ToString("D")));
    }

    [Fact]
    public void EmptyTenantAndMissingOrExcessiveReasonsCannotStartLifecycleWork()
    {
        Assert.Throws<ArgumentException>(() => TenantRetentionPolicy.Schedule(Guid.Empty, DateTimeOffset.UtcNow, false));
        Assert.Throws<ArgumentException>(() => TenantRetentionPolicy.RequireReason(" "));
        Assert.Throws<ArgumentException>(() => TenantRetentionPolicy.RequireReason(new string('x', 1025)));
    }
}
