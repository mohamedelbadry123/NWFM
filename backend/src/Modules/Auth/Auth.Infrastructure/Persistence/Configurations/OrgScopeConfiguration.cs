using Auth.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Auth.Infrastructure.Persistence.Configurations;

public sealed class OrgScopeConfiguration : IEntityTypeConfiguration<OrgScope>
{
    public void Configure(EntityTypeBuilder<OrgScope> builder)
    {
        builder.ToTable("OrgScopes");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.OwnerType).HasMaxLength(50).IsRequired();
        builder.Property(e => e.OwnerId).HasMaxLength(450).IsRequired();
        builder.Property(e => e.Level).HasMaxLength(50);
        builder.Property(e => e.Code).HasMaxLength(100);
        builder.Ignore(e => e.HasTerritory);

        builder.HasIndex(e => new { e.OwnerType, e.OwnerId });

        builder.HasMany(e => e.Departments).WithOne().HasForeignKey(d => d.OrgScopeId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(e => e.Departments).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(e => e.ActivityTypes).WithOne().HasForeignKey(a => a.OrgScopeId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(e => e.ActivityTypes).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public sealed class OrgScopeDepartmentConfiguration : IEntityTypeConfiguration<OrgScopeDepartment>
{
    public void Configure(EntityTypeBuilder<OrgScopeDepartment> builder)
    {
        builder.ToTable("OrgScopesDepartment");
        builder.HasKey(e => new { e.OrgScopeId, e.DepartmentCode });
        builder.Property(e => e.DepartmentCode).HasMaxLength(50).IsRequired();
    }
}

public sealed class OrgScopeActivityTypeConfiguration : IEntityTypeConfiguration<OrgScopeActivityType>
{
    public void Configure(EntityTypeBuilder<OrgScopeActivityType> builder)
    {
        builder.ToTable("OrgScopesActivityType");
        builder.HasKey(e => new { e.OrgScopeId, e.ActivityTypeCode });
        builder.Property(e => e.ActivityTypeCode).HasMaxLength(50).IsRequired();
    }
}
