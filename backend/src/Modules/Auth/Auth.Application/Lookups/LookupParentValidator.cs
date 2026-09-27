using Auth.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;
namespace Auth.Application.Lookups;

internal static class LookupParentValidator
{
    public static Task<bool> IsValidAsync(IAuthDbContext db, string type, string? parent, CancellationToken ct) => type switch
    {
        "Cbu" => db.Clusters.AnyAsync(x => x.Code == parent && x.IsActive, ct),
        "Branch" or "OperationArea" => db.Cbus.AnyAsync(x => x.Code == parent && x.IsActive, ct),
        "FieldActivityType" => db.Departments.AnyAsync(x => x.Code == parent && x.IsActive, ct),
        _ => Task.FromResult(true)
    };
}
