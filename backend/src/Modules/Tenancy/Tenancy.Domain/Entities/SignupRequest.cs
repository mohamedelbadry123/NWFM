namespace Tenancy.Domain.Entities;

/// <summary>
/// A self-service request for a workspace, from the landing page through to an active tenant. Also
/// the provisioning job's checkpoint: <see cref="LastCompletedStep"/> is what lets a retried run pick
/// up after the last step that finished instead of starting over.
/// </summary>
public sealed class SignupRequest
{
    public const int NameMaxLength = 200;
    public const int EmailMaxLength = 256;
    public const int PhoneMaxLength = 30;
    public const int ServerMaxLength = 253;
    public const int DatabaseNameMaxLength = 128;
    public const int ErrorMaxLength = 2000;
    public const int ApprovedByMaxLength = 256;
    public const int MaxEmailVerificationAttempts = 5;

    private readonly List<SignupProvisioningEvent> _events = [];

    private SignupRequest()
    {
    }

    public Guid Id { get; private set; }

    public string TenantKey { get; private set; } = default!;

    public string OrganizationName { get; private set; } = default!;

    public string AdminFullName { get; private set; } = default!;

    public string AdminEmail { get; private set; } = default!;

    public string? AdminPhone { get; private set; }

    public string PlanCode { get; private set; } = default!;

    public string Language { get; private set; } = TenantSettings.Languages.English;

    public TenantDatabaseMode DatabaseMode { get; private set; }

    public string? ByoServer { get; private set; }

    public int? ByoPort { get; private set; }

    public string? ByoDatabase { get; private set; }

    /// <summary>The database owner the control plane migrates with.</summary>
    public string? ByoUserName { get; private set; }

    public string? EncryptedByoPassword { get; private set; }

    /// <summary>The restricted principal the tenant API serves requests with; it holds no database role.</summary>
    public string? ByoRuntimeUserName { get; private set; }

    public string? EncryptedByoRuntimePassword { get; private set; }

    public bool ByoTrustServerCertificate { get; private set; }

    public string EmailVerificationTokenHash { get; private set; } = default!;

    public DateTime EmailVerificationExpiresAtUtc { get; private set; }

    public int EmailVerificationAttempts { get; private set; }

    public DateTime? EmailVerifiedAtUtc { get; private set; }

    public SignupStatus Status { get; private set; }

    /// <summary>Fixed when the request is made: a self-service signup waits for staff, one staff create themselves does not.</summary>
    public bool ApprovalRequired { get; private set; }

    public DateTime? ApprovedAtUtc { get; private set; }

    public string? ApprovedBy { get; private set; }

    public ProvisioningStep? LastCompletedStep { get; private set; }

    public Guid? TenantId { get; private set; }

    public string? LastError { get; private set; }

    public int ProvisioningAttempts { get; private set; }

    public string? ClientIp { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    public DateTime? CompletedAtUtc { get; private set; }

    public IReadOnlyCollection<SignupProvisioningEvent> Events => _events;

    public static SignupRequest Create(
        string tenantKey,
        string organizationName,
        string adminFullName,
        string adminEmail,
        string? adminPhone,
        string planCode,
        string? language,
        TenantDatabaseMode databaseMode,
        string emailVerificationTokenHash,
        DateTime emailVerificationExpiresAtUtc,
        string? clientIp,
        bool approvalRequired,
        DateTime utcNow)
    {
        var key = Entities.TenantKey.Normalize(tenantKey);

        if (!Entities.TenantKey.IsValid(key))
        {
            throw new DomainException("The workspace address is not valid.");
        }

        if (databaseMode == TenantDatabaseMode.Configuration)
        {
            throw new DomainException("A signup either brings its own database or has one created.");
        }

        if (string.IsNullOrWhiteSpace(organizationName) || string.IsNullOrWhiteSpace(adminFullName) || string.IsNullOrWhiteSpace(adminEmail))
        {
            throw new DomainException("Organisation name, administrator name and e-mail are required.");
        }

        return new SignupRequest
        {
            Id = Guid.NewGuid(),
            TenantKey = key,
            OrganizationName = organizationName.Trim(),
            AdminFullName = adminFullName.Trim(),
            AdminEmail = adminEmail.Trim(),
            AdminPhone = string.IsNullOrWhiteSpace(adminPhone) ? null : adminPhone.Trim(),
            PlanCode = planCode.Trim().ToLowerInvariant(),
            Language = language == TenantSettings.Languages.Arabic ? TenantSettings.Languages.Arabic : TenantSettings.Languages.English,
            DatabaseMode = databaseMode,
            EmailVerificationTokenHash = emailVerificationTokenHash,
            EmailVerificationExpiresAtUtc = emailVerificationExpiresAtUtc,
            Status = SignupStatus.PendingEmailVerification,
            ApprovalRequired = approvalRequired,
            ClientIp = clientIp,
            CreatedAtUtc = utcNow,
            UpdatedAtUtc = utcNow
        };
    }

    public bool IsEmailVerified => EmailVerifiedAtUtc is not null;

    /// <summary>True while staff still have to approve the request before it may be provisioned.</summary>
    public bool IsAwaitingApprovalGate => ApprovalRequired && ApprovedAtUtc is null;

    /// <summary>Confirms the e-mail. False on a wrong, expired or exhausted token; the caller does not say which.</summary>
    public bool VerifyEmail(string tokenHash, DateTime utcNow)
    {
        if (IsEmailVerified)
        {
            return string.Equals(EmailVerificationTokenHash, tokenHash, StringComparison.Ordinal);
        }

        if (EmailVerificationAttempts >= MaxEmailVerificationAttempts || EmailVerificationExpiresAtUtc <= utcNow)
        {
            return false;
        }

        if (!string.Equals(EmailVerificationTokenHash, tokenHash, StringComparison.Ordinal))
        {
            EmailVerificationAttempts++;
            UpdatedAtUtc = utcNow;
            return false;
        }

        EmailVerifiedAtUtc = utcNow;
        UpdatedAtUtc = utcNow;
        return true;
    }

    public void ReissueEmailVerification(string tokenHash, DateTime expiresAtUtc, DateTime utcNow)
    {
        if (IsEmailVerified)
        {
            throw new DomainException("The e-mail address is already confirmed.");
        }

        EmailVerificationTokenHash = tokenHash;
        EmailVerificationExpiresAtUtc = expiresAtUtc;
        EmailVerificationAttempts = 0;
        UpdatedAtUtc = utcNow;
    }

    public void SetBringYourOwnDatabase(string server, int? port, string database, string userName,
        string encryptedPassword, string runtimeUserName, string encryptedRuntimePassword,
        bool trustServerCertificate, DateTime utcNow)
    {
        if (DatabaseMode != TenantDatabaseMode.BringYourOwn)
        {
            throw new DomainException("This signup does not bring its own database.");
        }

        if (Status is SignupStatus.Provisioning or SignupStatus.Completed or SignupStatus.Rejected)
        {
            throw new DomainException("The database can no longer be changed for this signup.");
        }

        ByoServer = server.Trim();
        ByoPort = port;
        ByoDatabase = database.Trim();
        if (string.Equals(userName.Trim(), runtimeUserName.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            throw new DomainException("The runtime user must be a different principal from the database owner.");
        }

        ByoUserName = userName.Trim();
        EncryptedByoPassword = encryptedPassword;
        ByoRuntimeUserName = runtimeUserName.Trim();
        EncryptedByoRuntimePassword = encryptedRuntimePassword;
        ByoTrustServerCertificate = trustServerCertificate;
        UpdatedAtUtc = utcNow;
    }

    public bool HasBringYourOwnDatabase =>
        DatabaseMode == TenantDatabaseMode.BringYourOwn
        && !string.IsNullOrWhiteSpace(ByoServer)
        && !string.IsNullOrWhiteSpace(EncryptedByoPassword)
        && !string.IsNullOrWhiteSpace(EncryptedByoRuntimePassword);

    public void AwaitDatabase(string reason, DateTime utcNow)
    {
        Status = SignupStatus.AwaitingDatabase;
        SetError(reason);
        UpdatedAtUtc = utcNow;
    }

    /// <summary>Parks a confirmed request until staff review it.</summary>
    public void AwaitApproval(DateTime utcNow)
    {
        if (!IsEmailVerified)
        {
            throw new DomainException("Confirm the e-mail address before the request is reviewed.");
        }

        if (Status != SignupStatus.PendingEmailVerification)
        {
            throw new DomainException("This signup cannot wait for approval in its current state.");
        }

        Status = SignupStatus.AwaitingApproval;
        UpdatedAtUtc = utcNow;
    }

    /// <summary>
    /// Staff accept the request. A managed signup is queued at once; a bring-your-own one moves on to
    /// the database step unless the customer already gave its details.
    /// </summary>
    public void Approve(string approvedBy, DateTime utcNow)
    {
        if (Status != SignupStatus.AwaitingApproval)
        {
            throw new DomainException("Only a signup waiting for approval can be approved.");
        }

        if (string.IsNullOrWhiteSpace(approvedBy))
        {
            throw new DomainException("The approver is required.");
        }

        var approver = approvedBy.Trim();
        ApprovedAtUtc = utcNow;
        ApprovedBy = approver.Length > ApprovedByMaxLength ? approver[..ApprovedByMaxLength] : approver;

        if (DatabaseMode == TenantDatabaseMode.BringYourOwn && !HasBringYourOwnDatabase)
        {
            AwaitDatabase("Enter the connection details of your database.", utcNow);
            return;
        }

        Queue(utcNow);
    }

    /// <summary>Queues provisioning. Allowed once the e-mail is confirmed (and approved, when required), and again after a failure.</summary>
    public void Queue(DateTime utcNow)
    {
        if (!IsEmailVerified)
        {
            throw new DomainException("Confirm the e-mail address before the workspace is created.");
        }

        if (IsAwaitingApprovalGate)
        {
            throw new DomainException("The signup must be approved before the workspace is created.");
        }

        if (Status is SignupStatus.Completed or SignupStatus.Rejected or SignupStatus.Provisioning)
        {
            throw new DomainException("This signup cannot be queued in its current state.");
        }

        if (DatabaseMode == TenantDatabaseMode.BringYourOwn && !HasBringYourOwnDatabase)
        {
            throw new DomainException("Enter the database connection details first.");
        }

        Status = SignupStatus.Queued;
        UpdatedAtUtc = utcNow;
    }

    public void BeginProvisioning(DateTime utcNow)
    {
        if (Status is not (SignupStatus.Queued or SignupStatus.Failed or SignupStatus.Provisioning))
        {
            throw new DomainException("This signup is not queued for provisioning.");
        }

        Status = SignupStatus.Provisioning;
        ProvisioningAttempts++;
        LastError = null;
        UpdatedAtUtc = utcNow;
    }

    public bool IsStepDone(ProvisioningStep step) => LastCompletedStep is { } done && done >= step;

    public void AttachTenant(Guid tenantId) => TenantId = tenantId;

    public void CompleteStep(ProvisioningStep step, string? message, DateTime utcNow)
    {
        LastCompletedStep = step;
        _events.Add(SignupProvisioningEvent.Create(Id, step, true, message, utcNow));
        UpdatedAtUtc = utcNow;
    }

    public void Fail(ProvisioningStep step, string error, DateTime utcNow)
    {
        Status = DatabaseMode == TenantDatabaseMode.BringYourOwn && step == ProvisioningStep.ValidateDatabase
            ? SignupStatus.AwaitingDatabase
            : SignupStatus.Failed;
        SetError(error);
        _events.Add(SignupProvisioningEvent.Create(Id, step, false, LastError, utcNow));
        UpdatedAtUtc = utcNow;
    }

    public void Complete(DateTime utcNow)
    {
        Status = SignupStatus.Completed;
        CompletedAtUtc = utcNow;
        LastError = null;
        UpdatedAtUtc = utcNow;
    }

    public void Reject(string reason, DateTime utcNow)
    {
        if (Status is SignupStatus.Completed or SignupStatus.Provisioning)
        {
            throw new DomainException("A signup that is provisioning or complete cannot be rejected.");
        }

        Status = SignupStatus.Rejected;
        SetError(reason);
        UpdatedAtUtc = utcNow;
    }

    private void SetError(string error) =>
        LastError = error.Length > ErrorMaxLength ? error[..ErrorMaxLength] : error;
}

public sealed class SignupProvisioningEvent
{
    private SignupProvisioningEvent()
    {
    }

    public long Id { get; private set; }

    public Guid SignupRequestId { get; private set; }

    public ProvisioningStep Step { get; private set; }

    public bool Succeeded { get; private set; }

    public string? Message { get; private set; }

    public DateTime AtUtc { get; private set; }

    internal static SignupProvisioningEvent Create(Guid signupRequestId, ProvisioningStep step, bool succeeded,
        string? message, DateTime utcNow) => new()
        {
            SignupRequestId = signupRequestId,
            Step = step,
            Succeeded = succeeded,
            Message = message is { Length: > SignupRequest.ErrorMaxLength } ? message[..SignupRequest.ErrorMaxLength] : message,
            AtUtc = utcNow
        };
}
