using NWFM.Shared.Domain;
using NWFM.Shared.Exceptions;

namespace Auth.Domain.Entities;

public sealed class Team : Entity
{
    private Team() { }

    private Team(string name, string? mobile)
    {
        Name = name;
        Mobile = mobile;
        IsActive = true;
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    public string Name { get; private set; } = default!;
    public string? Mobile { get; private set; }
    public bool IsActive { get; private set; }

    public string? DeviceName { get; private set; }
    public string? DeviceUuid { get; private set; }
    public string? AppVersion { get; private set; }
    public string? DeviceOs { get; private set; }
    public double? LastActiveLatitude { get; private set; }
    public double? LastActiveLongitude { get; private set; }
    public DateTime? LastActiveAt { get; private set; }

    public static Team Create(string name, string? mobile)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("A team must have a name.");

        return new Team(name.Trim(), mobile?.Trim());
    }

    public void Deactivate()
    {
        IsActive = false;
        SetUpdated(DateTime.UtcNow);
    }

    public void Activate()
    {
        IsActive = true;
        SetUpdated(DateTime.UtcNow);
    }

    public void RecordDeviceSession(
        string? deviceName, string? deviceUuid, string? appVersion,
        string? deviceOs, double? latitude, double? longitude)
    {
        DeviceName = deviceName;
        DeviceUuid = deviceUuid;
        AppVersion = appVersion;
        DeviceOs = deviceOs;
        LastActiveLatitude = latitude;
        LastActiveLongitude = longitude;
        LastActiveAt = DateTime.UtcNow;
        SetUpdated(DateTime.UtcNow);
    }
}
