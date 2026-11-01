namespace Tenancy.Infrastructure.Services;

/// <summary>The platform identity on a host that has none: never authenticated.</summary>
internal sealed class AnonymousPlatformUser : IPlatformUser
{
    public Guid? Id => null;

    public string? Email => null;

    public string? DisplayName => null;

    public string? Role => null;

    public bool IsAuthenticated => false;
}
