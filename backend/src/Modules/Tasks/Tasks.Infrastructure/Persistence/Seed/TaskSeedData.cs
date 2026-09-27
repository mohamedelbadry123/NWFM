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
///
/// The leak-inspection tasks also come filled, so the task grid opens with computed columns to look
/// at (see <c>docs/computed-columns.md</c>): each fill goes through the form engine exactly as a
/// crew's would, and its computed values are recorded on the task.
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
        int CompletionSlaHours,
        string? DepartmentCode = null);

    /// <param name="Answers">A fill to record when the task is first seeded; null leaves it unfilled.</param>
    private sealed record TaskSeed(
        string TaskNumber,
        string TypeCode,
        string Title,
        string Priority,
        double Latitude,
        double Longitude,
        string Address,
        string BranchCode,
        IReadOnlyDictionary<string, object?>? Answers = null);

    private const string LeakInspectionType = "DEMO_LEAK_INSPECTION";

    private static Dictionary<string, object?> Leak(
        string material,
        decimal rate,
        decimal hours,
        int customers,
        bool hazard,
        decimal? meterStart = null,
        decimal? meterEnd = null) =>
        new()
        {
            ["pipe_material"] = material,
            ["leak_rate_lph"] = rate,
            ["hours_leaking"] = hours,
            ["customers_affected"] = customers,
            ["is_safety_hazard"] = hazard,
            ["meter_start"] = meterStart,
            ["meter_end"] = meterEnd,
        };

    private static readonly TypeSeed[] Types =
    [
        new("FIELD_SURVEY", "Field Survey", "مسح ميداني", "SRV-FIELD-SURVEY-002", 48, 24),
        new("DEMO_ALL_INPUTS", "Demo — All Inputs", "تجريبي — جميع الحقول", "DEMO-ALL-INPUT-TYPES", 24, 24),
        new(LeakInspectionType, "Leak Inspection (Computed Demo)", "فحص التسربات (تجريبي محسوب)", "DEMO-LEAK-INSPECTION", 24, 24, "10"),
    ];

    /// <summary>Riyadh city CBU; the branches are real rows in Auth's lookup seed.</summary>
    private const string RiyadhCbu = "RCBU";

    private static readonly TaskSeed[] Tasks =
    [
        new("TSK-DEMO-0001", "FIELD_SURVEY", "Meter survey — Al Moraba", TaskPriorities.Normal, 24.6634, 46.7106, "Al Moraba, Riyadh", "R-16"),
        new("TSK-DEMO-0002", "FIELD_SURVEY", "Meter survey — Al Rawabi", TaskPriorities.High, 24.6927, 46.7883, "Al Rawabi, Riyadh", "R-21"),
        new("TSK-DEMO-0003", "DEMO_ALL_INPUTS", "Inspection walkthrough — Shobra", TaskPriorities.Urgent, 24.5731, 46.8286, "Shobra, Riyadh", "R-22"),
        new("TSK-DEMO-0004", "DEMO_ALL_INPUTS", "Inspection walkthrough — Al Mourouj", TaskPriorities.Low, 24.7586, 46.6547, "Al Mourouj, Riyadh", "R-23"),

        // One of each computed outcome: Critical/Immediate, High/Same day, Medium, Low/Scheduled, and one left to fill.
        new("TSK-LEAK-0001", LeakInspectionType, "Burst main — Al Olaya", TaskPriorities.Urgent, 24.6905, 46.6852, "Al Olaya, Riyadh", "R-16",
            Leak("cast_iron", 1200m, 6m, 180, hazard: true)),
        new("TSK-LEAK-0002", LeakInspectionType, "Service line leak — Al Malaz", TaskPriorities.High, 24.6617, 46.7383, "Al Malaz, Riyadh", "R-21",
            Leak("steel", 650m, 12m, 20, hazard: false, meterStart: 10450m, meterEnd: 10458.4m)),
        new("TSK-LEAK-0003", LeakInspectionType, "Valve seep — Al Naseem", TaskPriorities.Normal, 24.7385, 46.8216, "Al Naseem, Riyadh", "R-22",
            Leak("pvc", 150m, 48m, 4, hazard: false)),
        new("TSK-LEAK-0004", LeakInspectionType, "Meter drip — Al Suwaidi", TaskPriorities.Low, 24.5920, 46.6618, "Al Suwaidi, Riyadh", "R-23",
            Leak("pvc", 12.5m, 72m, 1, hazard: false, meterStart: 2210m, meterEnd: 2210.9m)),
        new("TSK-LEAK-0005", LeakInspectionType, "Reported leak — Al Yasmin (not yet inspected)", TaskPriorities.Normal, 24.8230, 46.6391, "Al Yasmin, Riyadh", "R-16"),
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
            var type = await context.TaskTypes.Include(t => t.Forms).FirstOrDefaultAsync(t => t.Code == seed.Code, ct);

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
                    [form.Id], null, seed.DepartmentCode, seed.FillSlaHours, seed.CompletionSlaHours, SeedActor, utcNow);

                context.TaskTypes.Add(type);
                logger.LogInformation("Seeded task type {Code}.", seed.Code);
            }

            types[seed.Code] = type;
        }

        await context.SaveChangesAsync(ct);

        var toFill = new List<(FieldTask Task, IReadOnlyDictionary<string, object?> Answers)>();

        foreach (var seed in Tasks)
        {
            if (!types.TryGetValue(seed.TypeCode, out var type)
                || await context.Tasks.AnyAsync(t => t.TaskNumber == seed.TaskNumber, ct))
            {
                continue;
            }

            var drafts = new List<TaskFormDraft>();
            foreach (var typeForm in type.Forms.OrderBy(f => f.SortOrder))
            {
                if (await forms.FindPublishedAsync(typeForm.FormDefinitionId, ct) is { } form)
                {
                    drafts.Add(new TaskFormDraft(form.Id, form.CurrentVersionNo, TaskFormSources.Type, typeForm.IsC2mClosingForm));
                }
            }

            if (drafts.Count == 0)
            {
                continue;
            }

            var task = FieldTask.Create(
                new FieldTaskDraft
                {
                    TaskNumber = seed.TaskNumber,
                    TaskTypeId = type.Id,
                    Forms = drafts,
                    Title = seed.Title,
                    Priority = seed.Priority,
                    Location = new TaskLocation(
                        seed.Latitude, seed.Longitude, seed.Address, RiyadhCbu, seed.BranchCode, null, type.DepartmentCode),
                    FillSlaHours = type.FillSlaHours,
                    CompletionSlaHours = type.CompletionSlaHours,
                    CreatedBy = SeedActor,
                },
                utcNow);

            context.Tasks.Add(task);
            logger.LogInformation("Seeded task {TaskNumber}.", seed.TaskNumber);

            if (seed.Answers is not null)
            {
                toFill.Add((task, seed.Answers));
            }
        }

        await context.SaveChangesAsync(ct);

        // Filled only when first seeded: a fill is filed under the task, so it needs the task saved first.
        foreach (var (task, answers) in toFill)
        {
            await SeedFillAsync(context, forms, task, answers, logger, ct);
        }
    }

    /// <summary>
    /// Fills a seeded task's first form the way a crew's fill goes: stored by the form engine under
    /// the task, then recorded on the task with the computed columns it works out.
    /// </summary>
    private static async Task SeedFillAsync(
        TasksDbContext context,
        IFormGateway forms,
        FieldTask task,
        IReadOnlyDictionary<string, object?> answers,
        ILogger logger,
        CancellationToken ct)
    {
        var taskForm = task.OrderedForms[0];

        var stored = await forms.SubmitAsync(
            new FormSubmitRequest
            {
                FormId = taskForm.FormDefinitionId,
                VersionNo = taskForm.FormVersionNo,
                ContextType = TasksSchema.FormContextType,
                ContextId = task.Id.ToString("D"),
                ClientSubmissionId = task.Id,
                Answers = answers,
            },
            ct);

        if (stored.IsFailure)
        {
            logger.LogWarning(
                "Seeded task {TaskNumber} left unfilled: {Code} {Message}",
                task.TaskNumber,
                stored.Error.Code,
                stored.Error.Message);
            return;
        }

        var computed = (await forms.ComputeAsync(taskForm.FormDefinitionId, stored.Value.VersionNo, answers, ct))
            .Select(c => new TaskComputedValueDraft(c.Key, c.OutputType, c.Number, c.Text))
            .ToList();

        task.RecordFill(taskForm.FormDefinitionId, stored.Value.SubmissionId, SeedActor, DateTime.UtcNow, computed);
        await context.SaveChangesAsync(ct);

        logger.LogInformation("Seeded a fill of task {TaskNumber} ({Count} computed values).", task.TaskNumber, computed.Count);
    }
}
