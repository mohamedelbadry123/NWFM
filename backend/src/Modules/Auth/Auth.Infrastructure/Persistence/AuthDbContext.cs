using System.Reflection;
using Auth.Application.Common.Interfaces;
using Auth.Domain.Entities;
using Auth.Domain.Entities.Lookups;
using Auth.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Auth.Infrastructure.Persistence;

public class AuthDbContext : IdentityDbContext<ApplicationUser, ApplicationRole, Guid,
    IdentityUserClaim<Guid>, IdentityUserRole<Guid>, IdentityUserLogin<Guid>,
    IdentityRoleClaim<Guid>, IdentityUserToken<Guid>>, IAuthDbContext
{
    public AuthDbContext(DbContextOptions<AuthDbContext> options) : base(options) { }

    public DbSet<UserRefreshToken> UserRefreshTokens => Set<UserRefreshToken>();
    public DbSet<SsoAuthorizationCode> SsoAuthorizationCodes => Set<SsoAuthorizationCode>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<OrgScope> OrgScopes => Set<OrgScope>();
    public DbSet<Team> Teams => Set<Team>();
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<Cluster> Clusters => Set<Cluster>();
    public DbSet<Cbu> Cbus => Set<Cbu>();
    public DbSet<Branch> Branches => Set<Branch>();
    public DbSet<OperationArea> OperationAreas => Set<OperationArea>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.HasDefaultSchema("Auth");

        builder.Entity<ApplicationUser>(b => b.ToTable("Users"));
        builder.Entity<ApplicationRole>(b => b.ToTable("Roles"));
        builder.Entity<IdentityUserRole<Guid>>(b => b.ToTable("UserRoles"));
        builder.Entity<IdentityUserClaim<Guid>>(b => b.ToTable("UserClaims"));
        builder.Entity<IdentityRoleClaim<Guid>>(b => b.ToTable("RoleClaims"));
        builder.Entity<IdentityUserLogin<Guid>>(b => b.ToTable("UserLogins"));
        builder.Entity<IdentityUserToken<Guid>>(b => b.ToTable("UserTokens"));

        builder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
    }
}
