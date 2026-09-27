namespace Workflow.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Workflow.Domain.Entities;
using Workflow.Domain.Enums;

public sealed class SlaPolicyConfiguration : IEntityTypeConfiguration<SlaPolicy>
{
    public void Configure(EntityTypeBuilder<SlaPolicy> builder)
    {
        builder.ToTable("sla_policies", "Workflow");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.OrganizationId);
        builder.Property(x => x.DepartmentCode).HasMaxLength(50);
        builder.Property(x => x.FieldActivityCode).HasMaxLength(50);
        builder.HasIndex(x => new { x.OrganizationId, x.DepartmentCode, x.FieldActivityCode })
            .IsUnique().HasFilter("[IsActive] = 1 AND [DepartmentCode] IS NOT NULL AND [FieldActivityCode] IS NOT NULL");
        builder.Property(x => x.PolicyCode).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.NameAr).HasMaxLength(200);
        builder.Property(x => x.Duration).IsRequired();
        builder.Property(x => x.DurationUnit)
            .HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(x => x.BusinessCalendarId).IsRequired();
        builder.Property(x => x.ReminderThresholdsJson).HasMaxLength(4000);
        builder.Property(x => x.EscalationThresholdsJson).HasMaxLength(4000);
        builder.Property(x => x.EscalationAssignmentKey).HasMaxLength(100);
        builder.Property(x => x.IsActive).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.UpdatedAt);
        builder.Property(x => x.RowVersion).IsRowVersion();

        builder.HasIndex(x => x.PolicyCode).IsUnique();
        builder.HasIndex(x => x.OrganizationId);
        builder.HasIndex(x => x.BusinessCalendarId);
    }
}
