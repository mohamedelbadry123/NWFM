using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tenancy.Infrastructure.Persistence;

namespace Tenancy.Infrastructure.Security;

/// <summary>
/// Data Protection key ring stored in the catalog, so every API node can read secrets another node
/// encrypted. Configure an XML encryptor (certificate) in production so the ring is not plaintext at rest.
/// </summary>
internal sealed class CatalogXmlRepository(IServiceScopeFactory scopeFactory) : IXmlRepository
{
    public IReadOnlyCollection<XElement> GetAllElements()
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();

        return db.DataProtectionKeys
            .AsNoTracking()
            .Select(x => x.Xml)
            .ToList()
            .Select(XElement.Parse)
            .ToList();
    }

    public void StoreElement(XElement element, string friendlyName)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();

        db.DataProtectionKeys.Add(new CatalogDataProtectionKey
        {
            FriendlyName = friendlyName,
            Xml = element.ToString(SaveOptions.DisableFormatting)
        });

        db.SaveChanges();
    }
}
