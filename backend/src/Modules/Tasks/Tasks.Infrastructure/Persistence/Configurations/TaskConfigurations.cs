using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tasks.Domain.Constants;
using Tasks.Domain.Entities;

namespace Tasks.Infrastructure.Persistence.Configurations;

public sealed class TaskTypeConfiguration : IEntityTypeConfiguration<TaskType>
{
    public void Configure(EntityTypeBuilder<TaskType> builder)
    {
        builder.ToTable(TasksSchema.TaskTypes, TasksSchema.Name);
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.Code).HasMaxLength(TaskType.CodeMaxLength).IsRequired();
        builder.Property(x => x.NameEn).HasMaxLength(TaskType.NameMaxLength).IsRequired();
        builder.Property(x => x.NameAr).HasMaxLength(TaskType.NameMaxLength).IsRequired();
        builder.Property(x => x.DescriptionEn).HasMaxLength(TaskType.DescriptionMaxLength);
        builder.Property(x => x.DescriptionAr).HasMaxLength(TaskType.DescriptionMaxLength);
        builder.Property(x => x.DepartmentCode).HasMaxLength(TaskType.DepartmentCodeMaxLength);
        builder.Property(x => x.CreatedBy).HasMaxLength(TaskType.ActorMaxLength);
        builder.Property(x => x.UpdatedBy).HasMaxLength(TaskType.ActorMaxLength);
        builder.Property(x => x.RowVersion).IsRowVersion();

        builder.HasIndex(x => x.Code).IsUnique();
        builder.HasIndex(x => x.FormDefinitionId);
        builder.HasIndex(x => x.IsActive);
    }
}

public sealed class FieldTaskConfiguration : IEntityTypeConfiguration<FieldTask>
{
    public void Configure(EntityTypeBuilder<FieldTask> builder)
    {
        builder.ToTable(TasksSchema.Tasks, TasksSchema.Name);
        builder.HasKey(x => x.Id);

        // The entity assigns its own id, so an assignment or history row reached through the
        // navigation is tracked as new rather than as a row EF believes it has already seen.
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.TaskNumber).HasMaxLength(FieldTask.TaskNumberMaxLength).IsRequired();
        builder.Property(x => x.Source).HasMaxLength(TaskSources.MaxLength).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(TaskStatuses.MaxLength).IsRequired();
        builder.Property(x => x.Priority).HasMaxLength(TaskPriorities.MaxLength).IsRequired();
        builder.Property(x => x.Title).HasMaxLength(FieldTask.TitleMaxLength);
        builder.Property(x => x.Notes).HasMaxLength(FieldTask.NotesMaxLength);
        builder.Property(x => x.ExternalReference).HasMaxLength(FieldTask.ExternalReferenceMaxLength);
        builder.Property(x => x.AdditionalDataJson).IsRequired().HasDefaultValue("{}");
        builder.Property(x => x.Address).HasMaxLength(FieldTask.AddressMaxLength);
        builder.Property(x => x.CbuCode).HasMaxLength(FieldTask.OrgCodeMaxLength);
        builder.Property(x => x.BranchCode).HasMaxLength(FieldTask.OrgCodeMaxLength);
        builder.Property(x => x.OperationAreaCode).HasMaxLength(FieldTask.OrgCodeMaxLength);
        builder.Property(x => x.DepartmentCode).HasMaxLength(FieldTask.OrgCodeMaxLength);
        builder.Property(x => x.AssignedBy).HasMaxLength(FieldTask.ActorMaxLength);
        builder.Property(x => x.LastFilledBy).HasMaxLength(FieldTask.ActorMaxLength);
        builder.Property(x => x.CompletedBy).HasMaxLength(FieldTask.ActorMaxLength);
        builder.Property(x => x.ReturnReasonCode).HasMaxLength(TaskReturnReasons.MaxLength);
        builder.Property(x => x.ReturnReason).HasMaxLength(FieldTask.ReturnReasonMaxLength);
        builder.Property(x => x.ReturnedBy).HasMaxLength(FieldTask.ActorMaxLength);
        builder.Property(x => x.ExpiredBy).HasMaxLength(FieldTask.ActorMaxLength);
        builder.Property(x => x.CreatedBy).HasMaxLength(FieldTask.ActorMaxLength);
        builder.Property(x => x.UpdatedBy).HasMaxLength(FieldTask.ActorMaxLength);
        builder.Property(x => x.RowVersion).IsRowVersion();

        builder.HasIndex(x => x.TaskNumber).IsUnique();
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => new { x.CbuCode, x.Status });
        builder.HasIndex(x => new { x.BranchCode, x.Status });
        builder.HasIndex(x => x.OperationAreaCode);
        builder.HasIndex(x => x.DepartmentCode);
        builder.HasIndex(x => x.TaskTypeId);
        builder.HasIndex(x => x.FormDefinitionId);
        builder.HasIndex(x => x.CreatedAt);
        builder.HasIndex(x => x.DueDate);

        builder.HasOne<TaskType>()
            .WithMany()
            .HasForeignKey(x => x.TaskTypeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(x => x.Assignments)
            .WithOne()
            .HasForeignKey(x => x.FieldTaskId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(x => x.History)
            .WithOne()
            .HasForeignKey(x => x.FieldTaskId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(x => x.Assignments).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(x => x.History).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Ignore(x => x.ActiveAssignment);
        builder.Ignore(x => x.IsUnfilled);
    }
}

public sealed class TaskAssignmentConfiguration : IEntityTypeConfiguration<TaskAssignment>
{
    public void Configure(EntityTypeBuilder<TaskAssignment> builder)
    {
        builder.ToTable(TasksSchema.TaskAssignments, TasksSchema.Name);
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.Status).HasMaxLength(TaskAssignmentStatuses.MaxLength).IsRequired();
        builder.Property(x => x.AssignedBy).HasMaxLength(FieldTask.ActorMaxLength);
        builder.Property(x => x.Note).HasMaxLength(TaskAssignment.NoteMaxLength);

        builder.HasIndex(x => x.FieldTaskId);

        // The eligible-teams load count and a crew's own worklist both read by team.
        builder.HasIndex(x => new { x.TeamId, x.Status });
    }
}

public sealed class TaskStatusHistoryConfiguration : IEntityTypeConfiguration<TaskStatusHistory>
{
    public void Configure(EntityTypeBuilder<TaskStatusHistory> builder)
    {
        builder.ToTable(TasksSchema.TaskStatusHistory, TasksSchema.Name);
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.FromStatus).HasMaxLength(TaskStatuses.MaxLength);
        builder.Property(x => x.ToStatus).HasMaxLength(TaskStatuses.MaxLength).IsRequired();
        builder.Property(x => x.ChangedBy).HasMaxLength(FieldTask.ActorMaxLength);
        builder.Property(x => x.Note).HasMaxLength(TaskStatusHistory.NoteMaxLength);

        builder.HasIndex(x => new { x.FieldTaskId, x.ChangedDate });
    }
}
