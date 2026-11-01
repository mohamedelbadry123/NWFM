namespace Tenancy.Domain.Entities;

/// <summary>Where a tenant's database came from, which decides how its connection string is resolved.</summary>
public enum TenantDatabaseMode
{
    /// <summary>Created by the platform on a registered <see cref="DatabaseServer"/>.</summary>
    Managed = 1,

    /// <summary>The customer's own server; the encrypted connection string is all we hold.</summary>
    BringYourOwn = 2,

    /// <summary>Connection string kept in app configuration and referenced by name (the bootstrap workspace).</summary>
    Configuration = 3
}

public enum SubscriptionStatus
{
    Trial = 1,
    Active = 2,
    Suspended = 3,
    Expired = 4,
    Cancelled = 5
}

public enum SignupStatus
{
    /// <summary>Submitted; waiting for the e-mail address to be confirmed.</summary>
    PendingEmailVerification = 1,

    /// <summary>A bring-your-own request whose database is not reachable or not ready yet.</summary>
    AwaitingDatabase = 2,

    Queued = 3,
    Provisioning = 4,
    Completed = 5,
    Failed = 6,
    Rejected = 7,

    /// <summary>E-mail confirmed; waiting for platform staff to approve the request before anything is created.</summary>
    AwaitingApproval = 8
}

public enum ProvisioningStep
{
    ReserveTenant = 1,
    CreateDatabase = 2,
    ValidateDatabase = 3,
    Migrate = 4,
    Seed = 5,
    CreateAdministrator = 6,
    Activate = 7,
    SendActivationEmail = 8
}

public enum SupportAccessScope
{
    ReadOnly = 1,
    Full = 2
}
