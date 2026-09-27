using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NWFM.Shared.Integration.Forms;
using Tasks.Domain.Constants;
using Tasks.Domain.Entities;

namespace Tasks.Infrastructure.Persistence.Seed;

/// <summary>
/// Development examples: a task type for each seeded form, and a few unassigned tasks around Riyadh
/// to assign and fill. Idempotent by code and task number. Runs after the form engine's seed, whose
/// forms the types are bound to; a form not yet published is skipped with a log line, not an error.
/// </summary>
internal static class TaskSeedData
{
    private const string SeedActor = "system";

    private sealed record TypeSeed(
        string Code,
        string NameEn,
        string NameAr,
        string FormCode,
        int FillSlaHours,
        int CompletionSlaHours);

    private sealed record TaskSeed(
        string TaskNumber,
        string TypeCode,
        string Title,
        string Priority,
        double Latitude,
        double Longitude,
        string Address,
        string BranchCode);

    private static readonly TypeSeed[] Types =
    [
        new("FIELD_SURVEY", "Field Survey", "مسح ميداني", "SRV-FIELD-SURVEY-002", 48, 24),
        new("DEMO_ALL_INPUTS", "Demo — All Inputs", "تجريبي — جميع الحقول", "DEMO-ALL-INPUT-TYPES", 24, 24),
    ];

    /// <summary>Riyadh city CBU; the branches are real rows in Auth's lookup seed.</summary>
    private const string RiyadhCbu = "RCBU";

    private static readonly TaskSeed[] Tasks =
    [
        new("TSK-DEMO-0001", "FIELD_SURVEY", "Meter survey — Al Moraba", TaskPriorities.Normal, 24.6634, 46.7106, "Al Moraba, Riyadh", "R-16"),
        new("TSK-DEMO-0002", "FIELD_SURVEY", "Meter survey — Al Rawabi", TaskPriorities.High, 24.6927, 46.7883, "Al Rawabi, Riyadh", "R-21"),
        new("TSK-DEMO-0003", "DEMO_ALL_INPUTS", "Inspection walkthrough — Shobra", TaskPriorities.Urgent, 24.5731, 46.8286, "Shobra, Riyadh", "R-22"),
        new("TSK-DEMO-0004", "DEMO_ALL_INPUTS", "Inspection walkthrough — Al Mourouj", TaskPriorities.Low, 24.7586, 46.6547, "Al Mourouj, Riyadh", "R-23"),
    ];

    /// <summary>
    /// WFM's Action Taken codes and what each tells C2M, as the reference app seeds them: only OCUL01
    /// completes the activity; every other code cancels it and travels back as the cancel reason.
    /// Inserted when missing and never overwritten, so an edit made on the admin screen survives.
    /// </summary>
    private static readonly (string Code, string FaStatus, string? CancelReason, string NameEn, string NameAr)[] C2mActionMappings =
    [
        (C2mOperationStatuses.CompletedActionCode, C2mOperationStatuses.Completed, null, "Work completed", "تم إنجاز العمل"),
        ("MMFCNR1", C2mOperationStatuses.Cancelled, "MMFCNR1", "Lack of contracts", "عدم وجود عقود"),
        ("MMFCNR2", C2mOperationStatuses.Cancelled, "MMFCNR2", "Difficulty implementing due to obstacles", "صعوبة التنفيذ بسبب عوائق"),
        ("MMFCNR3", C2mOperationStatuses.Cancelled, "MMFCNR3", "Requires a new connection request", "يتطلب طلب توصيل جديد"),
        ("MMFCNR4", C2mOperationStatuses.Cancelled, "MMFCNR4", "Cannot obtain drilling permits", "تعذّر الحصول على تصاريح الحفر"),
    ];

    private static async Task SeedC2mActionMappingsAsync(TasksDbContext context, ILogger logger, DateTime utcNow, CancellationToken ct)
    {
        var existing = await context.C2mActionMappings.Select(m => m.ActionCode).ToListAsync(ct);
        var known = existing.ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var seed in C2mActionMappings.Where(m => !known.Contains(m.Code)))
        {
            context.C2mActionMappings.Add(C2mActionMapping.Create(
                seed.Code, seed.FaStatus, seed.CancelReason, null, seed.NameEn, seed.NameAr, true, SeedActor, utcNow));
            logger.LogInformation("Seeded C2M action mapping {Code}.", seed.Code);
        }

        await context.SaveChangesAsync(ct);
    }

    public static async Task SeedAsync(IServiceScopeFactory scopeFactory, ILogger logger, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TasksDbContext>();
        var forms = scope.ServiceProvider.GetRequiredService<IFormGateway>();

        var utcNow = DateTime.UtcNow;
        await SeedC2mActionMappingsAsync(context, logger, utcNow, ct);

        var types = new Dictionary<string, TaskType>(StringComparer.Ordinal);

        foreach (var seed in Types)
        {
            var type = await context.TaskTypes.FirstOrDefaultAsync(t => t.Code == seed.Code, ct);

            if (type is null)
            {
                var form = await forms.FindPublishedByCodeAsync(seed.FormCode, ct);
                if (form is null)
                {
                    logger.LogWarning("Skipped task type {Code}: its form {FormCode} is not published.", seed.Code, seed.FormCode);
                    continue;
                }

                type = TaskType.Create(
                    seed.Code, seed.NameEn, seed.NameAr, null, null,
                    form.Id, null, seed.FillSlaHours, seed.CompletionSlaHours, SeedActor, utcNow);

                context.TaskTypes.Add(type);
                logger.LogInformation("Seeded task type {Code}.", seed.Code);
            }

            types[seed.Code] = type;
        }

        await context.SaveChangesAsync(ct);

        foreach (var seed in Tasks)
        {
            if (!types.TryGetValue(seed.TypeCode, out var type)
                || await context.Tasks.AnyAsync(t => t.TaskNumber == seed.TaskNumber, ct))
            {
                continue;
            }

            var form = await forms.FindPublishedAsync(type.FormDefinitionId, ct);
            if (form is null)
            {
                continue;
            }

            context.Tasks.Add(FieldTask.Create(
                new FieldTaskDraft
                {
                    TaskNumber = seed.TaskNumber,
                    TaskTypeId = type.Id,
                    FormDefinitionId = form.Id,
                    FormVersionNo = form.CurrentVersionNo,
                    Title = seed.Title,
                    Priority = seed.Priority,
                    Location = new TaskLocation(seed.Latitude, seed.Longitude, seed.Address, RiyadhCbu, seed.BranchCode, null, null),
                    FillSlaHours = type.FillSlaHours,
                    CompletionSlaHours = type.CompletionSlaHours,
                    CreatedBy = SeedActor,
                },
                utcNow));

            logger.LogInformation("Seeded task {TaskNumber}.", seed.TaskNumber);
        }

        await context.SaveChangesAsync(ct);
    }
}
