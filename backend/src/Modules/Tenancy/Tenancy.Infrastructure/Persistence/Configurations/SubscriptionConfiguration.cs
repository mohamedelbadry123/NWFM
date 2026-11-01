using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tenancy.Domain.Entities;

namespace Tenancy.Infrastructure.Persistence.Configurations;

public sealed class PlanConfiguration : IEntityTypeConfiguration<Plan>
{
    public void Configure(EntityTypeBuilder<Plan> builder)
    {
        builder.ToTable("Plans");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Code).HasMaxLength(Plan.CodeMaxLength).IsRequired();
        builder.HasIndex(x => x.Code).IsUnique();
        builder.Property(x => x.NameEn).HasMaxLength(Plan.NameMaxLength).IsRequired();
        builder.Property(x => x.NameAr).HasMaxLength(Plan.NameMaxLength).IsRequired();
        builder.Property(x => x.DescriptionEn).HasMaxLength(Plan.DescriptionMaxLength);
        builder.Property(x => x.DescriptionAr).HasMaxLength(Plan.DescriptionMaxLength);
    }
}

public sealed class SubscriptionConfiguration : IEntityTypeConfiguration<Subscription>
{
    public void Configure(EntityTypeBuilder<Subscription> builder)
    {
        builder.ToTable("Subscriptions");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.HasIndex(x => x.TenantId).IsUnique();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.Notes).HasMaxLength(Subscription.NotesMaxLength);
        builder.HasOne(x => x.Plan).WithMany().HasForeignKey(x => x.PlanId).OnDelete(DeleteBehavior.Restrict);
    }
}
