
namespace Tenancy.Domain.Entities;

/// <summary>A customer organisation. The root of the master catalog: every other record hangs off a tenant.</summary>
public sealed class Tenant
{
    public const int NameMaxLength = 200;
    public const int ContactMaxLength = 200;
    public const int StatusReasonMaxLength = 500;
    public const int StoragePathMaxLength = 400;

    private readonly List<TenantHost> _hosts = [];

    private Tenant()
    {
    }

    private Tenant(Guid id, string key, string name, string contactName, string contactEmail, string? contactPhone,
        TenantSeedProfile seedProfile, DateTime utcNow)
    {
        Id = id;
        Key = key;
        Name = name;
        ContactName = contactName;
        ContactEmail = contactEmail;
        ContactPhone = contactPhone;
        SeedProfile = seedProfile;
        Status = TenantStatus.Provisioning;
        CreatedAtUtc = utcNow;
        StatusChangedAtUtc = utcNow;
        Settings = TenantSettings.Default();
    }

    public Guid Id { get; private set; }

    public string Key { get; private set; } = default!;

    public string Name { get; private set; } = default!;

    public TenantStatus Status { get; private set; }

    public string? StatusReason { get; private set; }

    public string ContactName { get; private set; } = default!;

    public string ContactEmail { get; private set; } = default!;

    public string? ContactPhone { get; private set; }

    public TenantSeedProfile SeedProfile { get; private set; }

    /// <summary>Explicit media root; null uses the per-tenant default folder.</summary>
    public string? StoragePath { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime? ActivatedAtUtc { get; private set; }

    public DateTime StatusChangedAtUtc { get; private set; }

    public TenantSettings Settings { get; private set; } = default!;

    public IReadOnlyCollection<TenantHost> Hosts => _hosts;

    public TenantDatabase? Database { get; private set; }

    public Subscription? Subscription { get; private set; }

    public static Tenant Create(
        string key,
        string name,
        string contactName,
        string contactEmail,
        string? contactPhone,
        TenantSeedProfile seedProfile,
        DateTime utcNow,
        Guid? id = null)
    {
        var normalizedKey = TenantKey.Normalize(key);

        // The catalog id is also the OrganizationId of every row in the tenant's database and its
        // boundary binding, so an existing deployment is adopted under the id it already carries.
        if (id == Guid.Empty)
        {
            throw new DomainException("A workspace id cannot be empty.");
        }

        if (!TenantKey.IsValid(normalizedKey))
        {
            throw new DomainException("A workspace key must be 3-32 lowercase letters, digits or single hyphens, starting with a letter.");
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("A workspace must have a name.");
        }

        if (string.IsNullOrWhiteSpace(contactEmail))
        {
            throw new DomainException("A workspace must have a contact e-mail.");
        }

        return new Tenant(id ?? Guid.NewGuid(), normalizedKey, name.Trim(), contactName?.Trim() ?? string.Empty, contactEmail.Trim(),
            string.IsNullOrWhiteSpace(contactPhone) ? null : contactPhone.Trim(), seedProfile, utcNow);
    }

    public void RenameKey(string key)
    {
        var normalizedKey = TenantKey.Normalize(key);

        if (!TenantKey.IsValid(normalizedKey))
        {
            throw new DomainException("A workspace key must be 3-32 lowercase letters, digits or single hyphens, starting with a letter.");
        }

        Key = normalizedKey;
    }

    public void UpdateProfile(string name, string contactName, string contactEmail, string? contactPhone)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("A workspace must have a name.");
        }

        if (string.IsNullOrWhiteSpace(contactEmail))
        {
            throw new DomainException("A workspace must have a contact e-mail.");
        }

        Name = name.Trim();
        ContactName = contactName?.Trim() ?? string.Empty;
        ContactEmail = contactEmail.Trim();
        ContactPhone = string.IsNullOrWhiteSpace(contactPhone) ? null : contactPhone.Trim();
    }

    public void UseStoragePath(string? storagePath) =>
        StoragePath = string.IsNullOrWhiteSpace(storagePath) ? null : storagePath.Trim();

    public void AddHost(string host)
    {
        var normalized = TenantHost.Normalize(host);

        if (_hosts.Any(x => x.Host == normalized))
        {
            return;
        }

        _hosts.Add(TenantHost.Create(Id, normalized));
    }

    public void RemoveHost(string host)
    {
        var normalized = TenantHost.Normalize(host);
        _hosts.RemoveAll(x => x.Host == normalized);
    }

    public void AttachDatabase(TenantDatabase database)
    {
        if (database.TenantId != Id)
        {
            throw new DomainException("The database belongs to another workspace.");
        }

        Database = database;
    }

    public void StartSubscription(Subscription subscription)
    {
        if (subscription.TenantId != Id)
        {
            throw new DomainException("The subscription belongs to another workspace.");
        }

        Subscription = subscription;
    }

    public void ChangeSettings(TenantSettings settings) => Settings = settings;

    public void Activate(DateTime utcNow)
    {
        if (Status == TenantStatus.Deleted)
        {
            throw new DomainException("A deleted workspace cannot be activated.");
        }

        if (Database is null)
        {
            throw new DomainException("A workspace cannot be activated before its database is attached.");
        }

        ActivatedAtUtc ??= utcNow;
        ChangeStatus(TenantStatus.Active, null, utcNow);
    }

    public void Suspend(string reason, DateTime utcNow)
    {
        if (Status is TenantStatus.Deleted or TenantStatus.Provisioning)
        {
            throw new DomainException("Only an active workspace can be suspended.");
        }

        ChangeStatus(TenantStatus.Suspended, reason, utcNow);
    }

    public void EnterMaintenance(string reason, DateTime utcNow)
    {
        if (Status == TenantStatus.Deleted)
        {
            throw new DomainException("A deleted workspace cannot enter maintenance.");
        }

        ChangeStatus(TenantStatus.Maintenance, reason, utcNow);
    }

    public void MarkDeleted(string reason, DateTime utcNow) => ChangeStatus(TenantStatus.Deleted, reason, utcNow);

    private void ChangeStatus(TenantStatus status, string? reason, DateTime utcNow)
    {
        Status = status;
        StatusReason = reason is null ? null : reason.Length > StatusReasonMaxLength ? reason[..StatusReasonMaxLength] : reason;
        StatusChangedAtUtc = utcNow;
    }
}
