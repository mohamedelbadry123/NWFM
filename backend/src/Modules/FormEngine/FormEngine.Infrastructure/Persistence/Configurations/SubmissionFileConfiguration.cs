using FormEngine.Domain.Constants;
using FormEngine.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FormEngine.Infrastructure.Persistence.Configurations;

public sealed class SubmissionFileConfiguration : IEntityTypeConfiguration<SubmissionFile>
{
    public void Configure(EntityTypeBuilder<SubmissionFile> builder)
    {
        builder.ToTable(FormEngineSchema.SubmissionFiles, FormEngineSchema.Name);
        builder.HasKey(x => x.Id);

        // The id is the public file handle, minted before the bytes are written.
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.FormDefinitionId).IsRequired();
        builder.Property(x => x.DataName).HasMaxLength(SubmissionFile.DataNameMaxLength).IsRequired();
        builder.Property(x => x.FileName).HasMaxLength(SubmissionFile.FileNameMaxLength).IsRequired();
        builder.Property(x => x.ContentType).HasMaxLength(SubmissionFile.ContentTypeMaxLength).IsRequired();
        builder.Property(x => x.SizeBytes).IsRequired();
        builder.Property(x => x.RelativePath).HasMaxLength(SubmissionFile.RelativePathMaxLength).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(SubmissionFile.StatusMaxLength).IsRequired();
        builder.Property(x => x.StorageKind)
            .HasMaxLength(SubmissionFile.StatusMaxLength)
            .IsRequired()
            .HasDefaultValue(SubmissionFileStorageKinds.Managed);
        builder.Property(x => x.ContextType).HasMaxLength(FormContextTypes.MaxLength);
        builder.Property(x => x.ContextId).HasMaxLength(FormContextTypes.MaxLength);
        builder.Property(x => x.UploadedBy).HasMaxLength(SubmissionFile.ActorMaxLength);
        builder.Property(x => x.IsActive).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.UpdatedAt).IsRequired();

        builder.HasOne<FormDefinition>()
            .WithMany()
            .HasForeignKey(x => x.FormDefinitionId)
            .OnDelete(DeleteBehavior.Restrict);

        // No foreign key to the submission: FE.Submissions is outside the EF model.
        builder.HasIndex(x => x.SubmissionId);
        builder.HasIndex(x => new { x.Status, x.CreatedAt });
        builder.HasIndex(x => x.FormDefinitionId);
    }
}
