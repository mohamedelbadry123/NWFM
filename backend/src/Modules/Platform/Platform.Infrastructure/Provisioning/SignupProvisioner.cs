using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Platform.Application.Common.Interfaces;
using Tenancy.Infrastructure.Persistence;

namespace Platform.Infrastructure.Provisioning;

/// <summary>
/// The signup state machine. Each step is skipped when the signup's checkpoint says it already ran,
/// and each step is itself re-runnable, so a failure — or a process that died mid-way — is resumed
/// by running the whole thing again.
/// </summary>
internal sealed class SignupProvisioner(
    CatalogDbContext catalog,
    ITenantStore tenantStore,
    ITenantDatabaseManager databaseManager,
    ITenantConnectionStringResolver connectionStrings,
    ITenantDatabaseVerification verification,
    ManagedDatabaseCreator managedDatabases,
    IByoDatabaseInspector byoInspector,
    TenantAdministratorProvisioner administrators,
    ITenantAdministratorDirectory administratorDirectory,
    ISecretProtector secretProtector,
    IPlatformAuditWriter audit,
    TimeProvider timeProvider,
    ILogger<SignupProvisioner> logger)
    : ISignupProvisioner
{
    private const string KeyTaken = "The workspace address was taken by another signup.";
    private const string PlanUnavailable = "The selected plan is no longer available.";
    private const string EmailDisabledMessage = "E-mail is switched off: the administrator's credentials were not sent. Use 'Resend credentials' on the workspace page once e-mail works.";
    private const string MockFolderMessage = "Credentials written to the mock e-mail folder (e-mail is mocked).";

    public async Task ProvisionAsync(Guid signupRequestId, CancellationToken cancellationToken)
    {
        var signup = await catalog.SignupRequests.FirstOrDefaultAsync(s => s.Id == signupRequestId, cancellationToken);

        if (signup is null || signup.Status is not (SignupStatus.Queued or SignupStatus.Failed or SignupStatus.Provisioning))
        {
            logger.LogInformation("Signup {SignupId} is not waiting for provisioning; nothing to do.", signupRequestId);
            return;
        }

        signup.BeginProvisioning(Now);
        await catalog.SaveChangesAsync(cancellationToken);

        var step = ProvisioningStep.ReserveTenant;

        try
        {
            var tenant = await ReserveTenantAsync(signup, cancellationToken);

            step = ProvisioningStep.CreateDatabase;
            await CreateDatabaseAsync(signup, tenant, cancellationToken);

            step = ProvisioningStep.ValidateDatabase;
            await ValidateDatabaseAsync(signup, tenant, cancellationToken);

            var info = Publish(tenant.Id);

            step = ProvisioningStep.Migrate;
            if (!signup.IsStepDone(step))
            {
                var version = await databaseManager.MigrateAsync(info, cancellationToken);
                tenant.Database!.RecordMigration(version, Now);
                await CompleteAsync(signup, step, version, cancellationToken);
            }

            step = ProvisioningStep.Seed;
            if (!signup.IsStepDone(step))
            {
                await databaseManager.SeedAsync(info, TenantSeedProfile.Standard, cancellationToken);
                await CompleteAsync(signup, step, null, cancellationToken);
            }

            step = ProvisioningStep.CreateAdministrator;
            var administratorId = await administrators.EnsureAdministratorAsync(info, signup.AdminEmail, signup.AdminPhone);
            if (!signup.IsStepDone(step))
            {
                await CompleteAsync(signup, step, signup.AdminEmail, cancellationToken);
            }

            step = ProvisioningStep.Activate;
            if (!signup.IsStepDone(step))
            {
                // The runtime credential must prove it is confined before the tenant serves anyone.
                if (await verification.VerifyRuntimeAsync(info, cancellationToken))
                    tenant.Database!.RecordVerification(Now);
                tenant.Activate(Now);
                await CompleteAsync(signup, step, null, cancellationToken);
                info = Publish(tenant.Id);
            }

            step = ProvisioningStep.SendActivationEmail;
            var delivery = await SendActivationEmailAsync(signup, info, administratorId, cancellationToken);
            signup.CompleteStep(step, DeliveryMessage(delivery), Now);
            if (delivery == TenantCredentialsDelivery.EmailDisabled)
            {
                audit.Add(PlatformAuditActions.CredentialsNotSent, tenant.Id, nameof(SignupRequest), signup.Id.ToString(), EmailDisabledMessage);
            }

            signup.Complete(Now);
            audit.Add(PlatformAuditActions.SignupProvisioned, tenant.Id, nameof(SignupRequest), signup.Id.ToString(), tenant.Key);
            await catalog.SaveChangesAsync(cancellationToken);

            logger.LogInformation("Signup {SignupId} provisioned as tenant {TenantKey}.", signup.Id, tenant.Key);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogError(ex, "Provisioning signup {SignupId} failed at {Step}.", signup.Id, step);
            await RecordFailureAsync(signup, step, ex, cancellationToken);
        }
    }

    private DateTime Now => timeProvider.GetUtcNow().UtcDateTime;

    private async Task<Tenant> ReserveTenantAsync(SignupRequest signup, CancellationToken cancellationToken)
    {
        if (signup.TenantId is { } existingId)
        {
            return await LoadTenantAsync(existingId, cancellationToken);
        }

        var holder = await catalog.Tenants
            .Include(t => t.Database)
            .Include(t => t.Subscription)
            .FirstOrDefaultAsync(t => t.Key == signup.TenantKey, cancellationToken);

        if (holder is not null)
        {
            // A previous run reserved the address and died before checkpointing: adopt it.
            if (!IsOrphanedReservation(holder, signup))
            {
                throw new ProvisioningException(KeyTaken);
            }

            await AttachReservationAsync(signup, holder, cancellationToken);
            return holder;
        }

        var plan = await catalog.Plans.FirstOrDefaultAsync(p => p.Code == signup.PlanCode && p.IsActive, cancellationToken)
            ?? throw new ProvisioningException(PlanUnavailable);

        var now = Now;
        var tenant = Tenant.Create(signup.TenantKey, signup.OrganizationName, signup.AdminFullName, signup.AdminEmail,
            signup.AdminPhone, TenantSeedProfile.Standard, now);
        tenant.ChangeSettings(TenantSettings.Create(sso: false, signup.Language));
        tenant.StartSubscription(Subscription.Start(tenant.Id, plan, now));

        catalog.Tenants.Add(tenant);
        audit.Add(PlatformAuditActions.TenantCreated, tenant.Id, nameof(Tenant), tenant.Key, $"From signup {signup.Id} on plan {plan.Code}");

        // The tenant is saved on its own first, so a lost race leaves nothing on the signup to undo.
        try
        {
            await catalog.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // The unique key on the tenant table: another signup won the race for this address.
            throw new ProvisioningException(KeyTaken);
        }

        await AttachReservationAsync(signup, tenant, cancellationToken);
        return tenant;
    }

    private static bool IsOrphanedReservation(Tenant tenant, SignupRequest signup) =>
        tenant.Status == TenantStatus.Provisioning
        && tenant.CreatedAtUtc >= signup.CreatedAtUtc
        && string.Equals(tenant.ContactEmail, signup.AdminEmail, StringComparison.OrdinalIgnoreCase);

    private async Task AttachReservationAsync(SignupRequest signup, Tenant tenant, CancellationToken cancellationToken)
    {
        signup.AttachTenant(tenant.Id);
        await CompleteAsync(signup, ProvisioningStep.ReserveTenant, tenant.Key, cancellationToken);
    }

    private async Task CreateDatabaseAsync(SignupRequest signup, Tenant tenant, CancellationToken cancellationToken)
    {
        if (signup.DatabaseMode == TenantDatabaseMode.BringYourOwn)
        {
            // Re-run until the database validates: a customer fixing their details replaces the
            // connection string the previous attempt stored.
            if (signup.IsStepDone(ProvisioningStep.ValidateDatabase))
            {
                return;
            }

            var details = ByoDetails(signup);
            var runtime = secretProtector.Protect(SecretPurposes.TenantRuntimeConnection, byoInspector.BuildRuntimeConnectionString(details));
            var owner = secretProtector.Protect(SecretPurposes.TenantAdminConnection, byoInspector.BuildConnectionString(details));

            if (tenant.Database is { } database)
            {
                database.ReplaceConnectionStrings(runtime, owner);
            }
            else
            {
                tenant.AttachDatabase(TenantDatabase.CreateBringYourOwn(
                    tenant.Id, signup.ByoDatabase, details.UserName, details.RuntimeUserName, runtime, owner));
            }

            await CompleteAsync(signup, ProvisioningStep.CreateDatabase, signup.ByoServer, cancellationToken);
            return;
        }

        if (signup.IsStepDone(ProvisioningStep.CreateDatabase) && tenant.Database is not null)
        {
            return;
        }

        if (tenant.Database is null)
        {
            tenant.AttachDatabase(await managedDatabases.CreateAsync(tenant, cancellationToken));
        }

        await CompleteAsync(signup, ProvisioningStep.CreateDatabase, tenant.Database!.DatabaseName, cancellationToken);
    }

    private async Task ValidateDatabaseAsync(SignupRequest signup, Tenant tenant, CancellationToken cancellationToken)
    {
        if (signup.IsStepDone(ProvisioningStep.ValidateDatabase))
        {
            return;
        }

        if (signup.DatabaseMode == TenantDatabaseMode.BringYourOwn)
        {
            var inspection = await byoInspector.InspectAsync(ByoDetails(signup), cancellationToken);
            if (!inspection.Succeeded)
            {
                throw new ProvisioningException(inspection.Summary);
            }
        }
        else
        {
            await using var connection = new SqlConnection(connectionStrings.Resolve(Publish(tenant.Id)));
            await connection.OpenAsync(cancellationToken);
        }

        await CompleteAsync(signup, ProvisioningStep.ValidateDatabase, null, cancellationToken);
    }

    /// <summary>
    /// A temporary password and a set-password link for the first administrator. An SMTP failure
    /// throws and fails the step, so Retry sends it again; a switched-off channel is recorded on the
    /// step instead of passing for a delivered e-mail.
    /// </summary>
    private async Task<TenantCredentialsDelivery> SendActivationEmailAsync(SignupRequest signup, TenantInfo tenant, string administratorId,
        CancellationToken cancellationToken)
    {
        var sent = await administratorDirectory.SendCredentialsAsync(
            new TenantCredentialsRequest(tenant, administratorId, signup.Language, signup.OrganizationName, TenantCredentialsEmail.NewWorkspace),
            cancellationToken);

        return sent.IsSuccess
            ? sent.Value
            : throw new ProvisioningException(sent.Error.Message);
    }

    private static string? DeliveryMessage(TenantCredentialsDelivery delivery) => delivery switch
    {
        TenantCredentialsDelivery.EmailDisabled => EmailDisabledMessage,
        TenantCredentialsDelivery.WrittenToMockFolder => MockFolderMessage,
        _ => null
    };

    private ByoDatabaseDetails ByoDetails(SignupRequest signup)
    {
        if (!signup.HasBringYourOwnDatabase)
        {
            throw new ProvisioningException("The database connection details are missing.");
        }

        return new ByoDatabaseDetails(
            signup.ByoServer!,
            signup.ByoPort,
            signup.ByoDatabase!,
            signup.ByoUserName!,
            secretProtector.Unprotect(SecretPurposes.SignupDatabasePassword, signup.EncryptedByoPassword!),
            signup.ByoRuntimeUserName!,
            secretProtector.Unprotect(SecretPurposes.SignupDatabasePassword, signup.EncryptedByoRuntimePassword!),
            signup.ByoTrustServerCertificate);
    }

    private async Task<Tenant> LoadTenantAsync(Guid tenantId, CancellationToken cancellationToken) =>
        await catalog.Tenants
            .Include(t => t.Database)
            .Include(t => t.Subscription)
            .FirstOrDefaultAsync(t => t.Id == tenantId, cancellationToken)
        ?? throw new ProvisioningException("The tenant reserved for this signup no longer exists.");

    /// <summary>Makes the catalog's latest state visible to the tenant store, which the tenant-scoped steps read.</summary>
    private TenantInfo Publish(Guid tenantId)
    {
        tenantStore.Invalidate();
        return tenantStore.FindById(tenantId) ?? throw new ProvisioningException("The tenant is not visible in the catalog.");
    }

    private async Task CompleteAsync(SignupRequest signup, ProvisioningStep step, string? message, CancellationToken cancellationToken)
    {
        signup.CompleteStep(step, message, Now);
        await catalog.SaveChangesAsync(cancellationToken);
    }

    private async Task RecordFailureAsync(SignupRequest signup, ProvisioningStep step, Exception ex, CancellationToken cancellationToken)
    {
        // Whatever the failed step left pending must not ride along with the failure record.
        foreach (var entry in catalog.ChangeTracker.Entries()
                     .Where(e => e.Entity is not SignupRequest and not SignupProvisioningEvent && e.State != EntityState.Unchanged)
                     .ToList())
        {
            entry.State = EntityState.Detached;
        }

        var message = ex is ProvisioningException ? ex.Message : $"{ex.GetType().Name}: {ex.Message}";
        signup.Fail(step, message, Now);
        audit.Add(PlatformAuditActions.SignupFailed, signup.TenantId, nameof(SignupRequest), signup.Id.ToString(), $"{step}: {message}");

        try
        {
            await catalog.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception saveError)
        {
            logger.LogError(saveError, "Recording the provisioning failure of signup {SignupId} failed.", signup.Id);
        }
    }

    private sealed class ProvisioningException(string message) : Exception(message);
}
