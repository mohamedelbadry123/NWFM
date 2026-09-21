using Microsoft.EntityFrameworkCore;
using Tasks.Domain.Entities;

namespace Tasks.Application.Common.Interfaces;

public interface ITasksDbContext
{
    DbSet<TaskType> TaskTypes { get; }
    DbSet<FieldTask> Tasks { get; }
    DbSet<TaskAssignment> TaskAssignments { get; }
    DbSet<TaskStatusHistory> TaskStatusHistory { get; }
    DbSet<C2mActionMapping> C2mActionMappings { get; }
    DbSet<C2mDispatchLog> C2mDispatchLogs { get; }

    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
