namespace Platform.Application.Common.Security;

/// <summary>
/// Marks a request as platform-staff only. Checked against the platform identity, never against
/// tenant roles or permissions — a tenant Administrator is not platform staff.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class PlatformAuthorizeAttribute : Attribute
{
    /// <summary>Comma-delimited platform roles; empty means any platform staff member.</summary>
    public string Roles { get; set; } = string.Empty;
}
