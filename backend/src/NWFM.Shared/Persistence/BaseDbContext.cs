using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using NWFM.Shared.Abstractions;
using NWFM.Shared.MultiTenancy;

namespace NWFM.Shared.Persistence;

public abstract class BaseDbContext : DbContext
{
    private readonly ICurrentTenant _currentTenant;
    protected ICurrentTenant CurrentTenant => _currentTenant;

    protected BaseDbContext(DbContextOptions options, ICurrentTenant currentTenant)
        : base(options)
    {
        _currentTenant = currentTenant;
    }

    private void ValidateTenantWrites()
    {
        foreach (var entry in ChangeTracker.Entries<ITenantAware>())
            if (entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
                if (entry.Entity.OrganizationId != CurrentTenant.OrganizationId || CurrentTenant.OrganizationId == Guid.Empty)
                    throw new InvalidOperationException("A record cannot be written outside the active tenant.");
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ValidateTenantWrites();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        ValidateTenantWrites();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (typeof(ITenantAware).IsAssignableFrom(entityType.ClrType))
            {
                modelBuilder.Entity(entityType.ClrType)
                    .HasQueryFilter(BuildTenantFilter(entityType.ClrType));
            }
        }

        base.OnModelCreating(modelBuilder);
    }

    private LambdaExpression BuildTenantFilter(Type entityType)
    {
        var param = Expression.Parameter(entityType, "e");
        var tenantId = Expression.Property(param, nameof(ITenantAware.OrganizationId));

        // Expression.Constant(this) is rebound by EF Core to the current DbContext instance
        // at query execution time, so _currentTenant resolves per-request rather than using
        // the stale instance captured when OnModelCreating ran during startup.
        var dbContextRef = Expression.Constant(this);
        var currentTenantField = typeof(BaseDbContext).GetField(
            "_currentTenant",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var currentTenantAccess = Expression.Field(dbContextRef, currentTenantField);
        var currentOrgId = Expression.Property(currentTenantAccess, nameof(ICurrentTenant.OrganizationId));

        var body = Expression.Equal(tenantId, currentOrgId);
        return Expression.Lambda(body, param);
    }
}
