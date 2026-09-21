namespace NWFM.Tests.Modules.Tasks;

using global::Tasks.Domain.Entities;
using global::Tasks.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

/// <summary>Shared fixtures for the Tasks tests.</summary>
internal static class TaskTestData
{
    public static readonly DateTime Now = new(2026, 9, 21, 8, 0, 0, DateTimeKind.Utc);

    public static readonly Guid FormId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public static TasksDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<TasksDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new TasksDbContext(options);
    }

    public static TaskType Type(Guid? formId = null) =>
        TaskType.Create("SURVEY", "Survey", "مسح", null, null, formId ?? FormId, null, 48, 24, "tester", Now);

    /// <summary>A task in Riyadh city, branch R-16, department 10.</summary>
    public static FieldTask Task(TaskType type, string number = "TSK-1", string branch = "R-16", string? department = "10") =>
        FieldTask.Create(
            new FieldTaskDraft
            {
                TaskNumber = number,
                TaskTypeId = type.Id,
                FormDefinitionId = type.FormDefinitionId,
                FormVersionNo = 1,
                Title = "Meter survey",
                Location = new TaskLocation(24.66, 46.71, "Al Moraba", "RCBU", branch, null, department),
                FillSlaHours = type.FillSlaHours,
                CompletionSlaHours = type.CompletionSlaHours,
                CreatedBy = "tester",
            },
            Now);
}
