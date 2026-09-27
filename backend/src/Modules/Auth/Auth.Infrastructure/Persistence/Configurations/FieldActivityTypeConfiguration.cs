using Auth.Domain.Entities.Lookups;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Auth.Infrastructure.Persistence.Configurations;

public sealed class FieldActivityTypeConfiguration : IEntityTypeConfiguration<FieldActivityType>
{
    public void Configure(EntityTypeBuilder<FieldActivityType> builder)
    {
        builder.ToTable("LKP_FIELD_ACTIVITY_TYPE");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Code).HasMaxLength(50).IsRequired();
        builder.Property(x => x.DepartmentCode).HasMaxLength(50).IsRequired();
        builder.Property(x => x.NameEn).HasMaxLength(200).IsRequired();
        builder.Property(x => x.NameAr).HasMaxLength(200).IsRequired();
        builder.HasIndex(x => new { x.DepartmentCode, x.Code }).IsUnique();
        builder.HasOne<Department>().WithMany().HasForeignKey(x => x.DepartmentCode)
            .HasPrincipalKey(x => x.Code).OnDelete(DeleteBehavior.Restrict);
    }
}
