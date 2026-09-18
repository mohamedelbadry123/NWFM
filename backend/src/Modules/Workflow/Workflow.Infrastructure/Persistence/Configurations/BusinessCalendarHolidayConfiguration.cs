namespace Workflow.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Workflow.Domain.Entities;

public sealed class BusinessCalendarHolidayConfiguration : IEntityTypeConfiguration<BusinessCalendarHoliday>
{
    public void Configure(EntityTypeBuilder<BusinessCalendarHoliday> builder)
    {
        builder.ToTable("business_calendar_holidays", "Workflow");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.CalendarId).IsRequired();
        builder.Property(x => x.HolidayDate).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.NameAr).HasMaxLength(200);
        builder.Property(x => x.IsRecurring).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.UpdatedAt);

        builder.HasIndex(x => new { x.CalendarId, x.HolidayDate });
    }
}
