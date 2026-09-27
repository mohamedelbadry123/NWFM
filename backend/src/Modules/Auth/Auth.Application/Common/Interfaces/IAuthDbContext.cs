using Auth.Domain.Entities;
using Auth.Domain.Entities.Lookups;
using Microsoft.EntityFrameworkCore;

namespace Auth.Application.Common.Interfaces;

public interface IAuthDbContext
{
    DbSet<Permission> Permissions { get; }
    DbSet<RolePermission> RolePermissions { get; }
    DbSet<OrgScope> OrgScopes { get; }
    DbSet<Team> Teams { get; }
    DbSet<Department> Departments { get; }
    DbSet<FieldActivityType> FieldActivityTypes { get; }
    DbSet<Cluster> Clusters { get; }
    DbSet<Cbu> Cbus { get; }
    DbSet<Branch> Branches { get; }
    DbSet<OperationArea> OperationAreas { get; }
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
