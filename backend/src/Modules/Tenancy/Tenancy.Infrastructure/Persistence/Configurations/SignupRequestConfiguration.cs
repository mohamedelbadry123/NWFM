using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tenancy.Domain.Entities;

namespace Tenancy.Infrastructure.Persistence.Configurations;

public sealed class SignupRequestConfiguration : IEntityTypeConfiguration<SignupRequest>
{
    public void Configure(EntityTypeBuilder<SignupRequest> builder)
    {
        builder.ToTable("SignupRequests");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.TenantKey).HasMaxLength(TenantKey.MaxLength).IsRequired();
        builder.HasIndex(x => x.TenantKey);
        builder.Property(x => x.OrganizationName).HasMaxLength(SignupRequest.NameMaxLength).IsRequired();
        builder.Property(x => x.AdminFullName).HasMaxLength(SignupRequest.NameMaxLength).IsRequired();
        builder.Property(x => x.AdminEmail).HasMaxLength(SignupRequest.EmailMaxLength).IsRequired();
        builder.HasIndex(x => x.AdminEmail);
        builder.Property(x => x.AdminPhone).HasMaxLength(SignupRequest.PhoneMaxLength);
        builder.Property(x => x.PlanCode).HasMaxLength(Plan.CodeMaxLength).IsRequired();
        builder.Property(x => x.Language).HasMaxLength(8).IsRequired();
        builder.Property(x => x.DatabaseMode).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.ByoServer).HasMaxLength(SignupRequest.ServerMaxLength);
        builder.Property(x => x.ByoDatabase).HasMaxLength(SignupRequest.DatabaseNameMaxLength);
        builder.Property(x => x.ByoUserName).HasMaxLength(SignupRequest.DatabaseNameMaxLength);
        builder.Property(x => x.EncryptedByoPassword).HasMaxLength(2000);
        builder.Property(x => x.ByoRuntimeUserName).HasMaxLength(SignupRequest.DatabaseNameMaxLength);
        builder.Property(x => x.EncryptedByoRuntimePassword).HasMaxLength(2000);
        builder.Property(x => x.EmailVerificationTokenHash).HasMaxLength(128).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
        builder.HasIndex(x => x.Status);
        builder.Property(x => x.ApprovedBy).HasMaxLength(SignupRequest.ApprovedByMaxLength);
        builder.Property(x => x.LastCompletedStep).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.LastError).HasMaxLength(SignupRequest.ErrorMaxLength);
        builder.Property(x => x.ClientIp).HasMaxLength(64);

        builder.HasMany(x => x.Events).WithOne().HasForeignKey(x => x.SignupRequestId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.Events).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public sealed class SignupProvisioningEventConfiguration : IEntityTypeConfiguration<SignupProvisioningEvent>
{
    public void Configure(EntityTypeBuilder<SignupProvisioningEvent> builder)
    {
        builder.ToTable("SignupProvisioningEvents");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Step).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.Message).HasMaxLength(SignupRequest.ErrorMaxLength);
    }
}
