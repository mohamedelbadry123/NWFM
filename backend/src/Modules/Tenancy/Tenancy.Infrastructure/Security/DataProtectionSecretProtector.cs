using Microsoft.AspNetCore.DataProtection;

namespace Tenancy.Infrastructure.Security;

internal sealed class DataProtectionSecretProtector(IDataProtectionProvider provider) : ISecretProtector
{
    public string Protect(string purpose, string plaintext) =>
        provider.CreateProtector(purpose).Protect(plaintext);

    public string Unprotect(string purpose, string protectedText) =>
        provider.CreateProtector(purpose).Unprotect(protectedText);
}
