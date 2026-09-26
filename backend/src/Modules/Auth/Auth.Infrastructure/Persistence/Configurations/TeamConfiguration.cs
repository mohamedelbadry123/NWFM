using Auth.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Auth.Infrastructure.Persistence.Configurations;

public sealed class TeamConfiguration : IEntityTypeConfiguration<Team>
{
    public void Configure(EntityTypeBuilder<Team> builder)
    {
        builder.ToTable("Teams");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Name).HasMaxLength(200).IsRequired();
        builder.Property(e => e.Mobile).HasMaxLength(20);
        builder.Property(e => e.DeviceName).HasMaxLength(200);
        builder.Property(e => e.DeviceUuid).HasMaxLength(200);
        builder.Property(e => e.AppVersion).HasMaxLength(50);
        builder.Property(e => e.DeviceOs).HasMaxLength(100);
    }
}
