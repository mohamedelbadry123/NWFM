namespace Workflow.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Workflow.Domain.Entities;

public sealed class BusinessCalendarConfiguration : IEntityTypeConfiguration<BusinessCalendar>
{
    public void Configure(EntityTypeBuilder<BusinessCalendar> builder)
    {
        builder.ToTable("business_calendars", "Workflow");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.OrganizationId);
        builder.Property(x => x.Code).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.NameAr).HasMaxLength(200);
        builder.Property(x => x.TimeZone).HasMaxLength(100).IsRequired();
        builder.Property(x => x.IsActive).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.UpdatedAt);
        builder.Property(x => x.RowVersion).IsRowVersion();

        builder.HasIndex(x => x.Code)
            .IsUnique()
            .HasFilter("[IsActive] = 1");

        builder.HasIndex(x => x.OrganizationId);

        builder.HasMany(x => x.Periods)
            .WithOne(x => x.Calendar)
            .HasForeignKey(x => x.CalendarId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(x => x.Holidays)
            .WithOne(x => x.Calendar)
            .HasForeignKey(x => x.CalendarId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
