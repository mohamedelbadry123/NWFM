using System.Reflection;
using FormEngine.Application.Common.Interfaces;
using FormEngine.Domain.Constants;
using FormEngine.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FormEngine.Infrastructure.Persistence.Seed;

/// <summary>
/// Seeds the example forms, published through <see cref="IFormPublisher"/> so each one registers
/// its fields and creates its own submission table exactly as a publish from the UI
/// would. Idempotent: a form already published is left alone, and one left as a draft by a failed
/// run is published on the next start.
/// </summary>
internal static class FormSeedData
{
    private const string SeedActor = "system";

    private const string ResourceNamespace = "FormEngine.Infrastructure.Persistence.Seed.Forms.";

    /// <param name="ResourceFile">An embedded form-builder document; see the Forms folder.</param>
    /// <param name="DepartmentCode">The owning department (Auth's lookup seed); null for none.</param>
    /// <param name="FieldActivityCode">One of that department's field activities; null for none.</param>
    /// <param name="Publish">
    /// False for a form deliberately left as a never-published draft — it shows how a draft filed under a field
    /// activity is listed but not offered as usable. It is created once and never published by the seed.
    /// </param>
    private sealed record FormSeed(
        string Code,
        string NameEn,
        string NameAr,
        string Category,
        string ResourceFile,
        string? DepartmentCode = null,
        string? FieldActivityCode = null,
        bool Publish = true);

    /// <summary>The computed-columns demo; see <c>docs/computed-columns.md</c>.</summary>
    public const string LeakInspectionCode = "DEMO-LEAK-INSPECTION";

    /// <summary>A second published form for Water Network / Leak repair, so an activity with that context lists several.</summary>
    public const string LeakRepairCompletionCode = "DEMO-LEAK-REPAIR-COMPLETION";

    /// <summary>A never-published draft for the same context: discoverable, but not usable until someone publishes it.</summary>
    public const string LeakRepairChecklistDraftCode = "DEMO-LEAK-REPAIR-CHECKLIST";

    private static readonly FormSeed[] Forms =
    [
        new(
            "SRV-FIELD-SURVEY-002",
            "Field Survey — Fulcrum Import",
            "المسح الميداني — استيراد فولكرم",
            FormCategories.Survey,
            "fulcrum-field-survey-import.json"),
        new(
            "DEMO-ALL-INPUT-TYPES",
            "All Input Types (Demo)",
            "جميع أنواع الحقول (نموذج تجريبي)",
            FormCategories.General,
            "all-input-types.json"),
        new(
            LeakInspectionCode,
            "Leak Inspection (Computed Columns Demo)",
            "فحص التسربات (نموذج الأعمدة المحسوبة)",
            FormCategories.Inspection,
            "leak-inspection-computed.json",
            "10",
            "LEAK_REPAIR"),
        new(
            LeakRepairCompletionCode,
            "Leak Repair Completion Report",
            "تقرير إنجاز إصلاح التسرب",
            FormCategories.Inspection,
            "leak-repair-completion.json",
            "10",
            "LEAK_REPAIR"),
        new(
            LeakRepairChecklistDraftCode,
            "Leak Repair Safety Checklist (Draft)",
            "قائمة التحقق من سلامة إصلاح التسرب (مسودة)",
            FormCategories.Checklist,
            "leak-repair-safety-checklist.json",
            "10",
            "LEAK_REPAIR",
            Publish: false),
    ];

    /// <summary>
    /// Each form is seeded in its own scope. Publishing touches the change tracker and the form's
    /// table, so a failure on one form must not leave half-applied state for the next.
    /// </summary>
    public static async Task SeedAsync(IServiceScopeFactory scopeFactory, ILogger logger, CancellationToken ct = default)
    {
        foreach (var seed in Forms)
        {
            try
            {
                await SeedFormAsync(scopeFactory, seed, logger, ct);
            }
            catch (Exception ex)
            {
                // A seed is example data: log it and carry on, rather than stopping the API from starting.
                logger.LogError(ex, "Failed to seed form {Code}.", seed.Code);
            }
        }
    }

    private static async Task SeedFormAsync(
        IServiceScopeFactory scopeFactory,
        FormSeed seed,
        ILogger logger,
        CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<FormEngineDbContext>();
        var publisher = scope.ServiceProvider.GetRequiredService<IFormPublisher>();

        var form = await context.FormDefinitions.FirstOrDefaultAsync(x => x.Code == seed.Code, ct);

        // Anything already published is left alone, and so is an existing draft the seed never publishes — both may
        // have been edited since.
        if (form is { CurrentVersionNo: not null } || (form is not null && !seed.Publish))
        {
            return;
        }

        var utcNow = DateTime.UtcNow;

        if (form is null)
        {
            var schemaJson = await ReadResourceAsync(seed.ResourceFile, ct);

            form = FormDefinition.Create(
                seed.Code, seed.NameEn, seed.NameAr, seed.Category, seed.DepartmentCode, seed.FieldActivityCode, SeedActor, utcNow);
            form.SetSchema(schemaJson, seed.NameEn, seed.NameAr, SeedActor, utcNow);

            context.FormDefinitions.Add(form);
            await context.SaveChangesAsync(ct);

            logger.LogInformation("Seeded form {Code}.", seed.Code);
        }

        if (!seed.Publish)
        {
            return;
        }

        var published = await publisher.PublishAsync(form.Id, SeedActor, ct);

        if (published.IsFailure)
        {
            logger.LogError(
                "Failed to publish seeded form {Code}: {Code2} {Message}",
                seed.Code,
                published.Error.Code,
                published.Error.Message);

            return;
        }

        logger.LogInformation("Published seeded form {Code} as version {VersionNo}.", seed.Code, published.Value.CurrentVersionNo);
    }

    private static async Task<string> ReadResourceAsync(string fileName, CancellationToken ct)
    {
        var resourceName = ResourceNamespace + fileName;

        await using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded form seed '{resourceName}' was not found.");

        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync(ct);
    }
}
