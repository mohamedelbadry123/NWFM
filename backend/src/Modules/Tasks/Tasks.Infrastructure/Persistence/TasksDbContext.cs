using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Tasks.Application.Common.Interfaces;
using Tasks.Domain.Constants;
using Tasks.Domain.Entities;

namespace Tasks.Infrastructure.Persistence;

/// <summary>
/// The Tasks module's own context, in the <c>TK</c> schema. A task's answers are not here: they live
/// in its form's submission table, owned by the form engine, and are reached through
/// <c>IFormGateway</c>.
/// </summary>
public sealed class TasksDbContext(DbContextOptions<TasksDbContext> options) : DbContext(options), ITasksDbContext
{
    public DbSet<TaskType> TaskTypes => Set<TaskType>();
    public DbSet<FieldTask> Tasks => Set<FieldTask>();
    public DbSet<TaskAssignment> TaskAssignments => Set<TaskAssignment>();
    public DbSet<TaskStatusHistory> TaskStatusHistory => Set<TaskStatusHistory>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.HasDefaultSchema(TasksSchema.Name);
        builder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
    }
}
