namespace Workflow.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Workflow.Domain.Entities;

public sealed class BusinessCalendarPeriodConfiguration : IEntityTypeConfiguration<BusinessCalendarPeriod>
{
    public void Configure(EntityTypeBuilder<BusinessCalendarPeriod> builder)
    {
        builder.ToTable("business_calendar_periods", "Workflow");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.CalendarId).IsRequired();
        builder.Property(x => x.DayOfWeek)
            .HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.StartTime).IsRequired();
        builder.Property(x => x.EndTime).IsRequired();
        builder.Property(x => x.IsWorkingTime).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.UpdatedAt);

        builder.HasIndex(x => new { x.CalendarId, x.DayOfWeek });
    }
}
