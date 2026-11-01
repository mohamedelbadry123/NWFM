namespace NWFM.Shared.Security;

/// <summary>
/// Marks a write that a read-only support session may still run (for example ending the session
/// itself). Absent this, every <c>*Command</c> is refused while <c>support_scope=readonly</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class AllowSupportReadOnlyAttribute : Attribute;
