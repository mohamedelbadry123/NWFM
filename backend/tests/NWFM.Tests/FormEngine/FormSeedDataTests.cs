namespace NWFM.Tests.Modules.FormEngine;

using System.Reflection;
using FluentAssertions;
using global::FormEngine.Application.Common.Schema;
using global::FormEngine.Domain.Constants;
using global::FormEngine.Infrastructure;

/// <summary>
/// The seeded forms are shipped JSON, so a typo in one is only found when it fails to publish on a
/// developer's machine. These read the embedded resources the way the seeder does.
/// </summary>
public sealed class FormSeedDataTests
{
    private const string ResourceNamespace = "FormEngine.Infrastructure.Persistence.Seed.Forms.";

    private static string ReadResource(string fileName)
    {
        var assembly = typeof(DependencyInjection).Assembly;

        using var stream = assembly.GetManifestResourceStream(ResourceNamespace + fileName);
        stream.Should().NotBeNull($"the seed '{fileName}' must be embedded in the Infrastructure assembly");

        using var reader = new StreamReader(stream!);
        return reader.ReadToEnd();
    }

    [Theory]
    [InlineData("fulcrum-field-survey-import.json")]
    [InlineData("all-input-types.json")]
    [InlineData("leak-inspection-computed.json")]
    [InlineData("leak-repair-completion.json")]
    [InlineData("leak-repair-safety-checklist.json")]
    public void EverySeed_IsValidAndPublishable(string fileName)
    {
        var json = ReadResource(fileName);

        FormSchemaParser.IsValidJson(json).Should().BeTrue();
        FormSchemaParser.HasElements(json).Should().BeTrue();

        var schema = FormSchemaParser.Parse(json);

        // What publish checks: legal identifiers, no duplicate column, nothing reserved.
        schema.Fields.Should().OnlyContain(f => FormDataName.IsValid(f.DataName));

        var writable = FormWritableFields.Of(schema).Select(f => f.Name).ToList();
        writable.Should().OnlyHaveUniqueItems();
        writable.Should().NotContain(name => FormSubmissionColumns.IsBase(name));

        // A seed whose computed columns the designer would refuse would also refuse to publish.
        FormComputedColumnValidator.Validate(schema).Should().BeEmpty();
    }

    /// <summary>The demo's computed columns, against the answers the task seed fills its tasks with.</summary>
    [Theory]
    [InlineData(1200, 6, 180, true, null, null, "Critical", 7.2, "Immediate", null)]
    [InlineData(650, 12, 20, false, 10450.0, 10458.4, "High", 7.8, "Same day", 8.4)]
    [InlineData(150, 48, 4, false, null, null, "Medium", 7.2, "Same day", null)]
    [InlineData(12.5, 72, 1, false, 2210.0, 2210.9, "Low", 0.9, "Scheduled", 0.9)]
    public void LeakInspectionSeed_WorksOutItsComputedColumns(
        double rate,
        double hours,
        int customers,
        bool hazard,
        double? meterStart,
        double? meterEnd,
        string severity,
        double waterLost,
        string response,
        double? meteredUse)
    {
        var schema = FormSchemaParser.Parse(ReadResource("leak-inspection-computed.json"));
        var answers = new Dictionary<string, object?>
        {
            ["pipe_material"] = "pvc",
            ["leak_rate_lph"] = (decimal)rate,
            ["hours_leaking"] = (decimal)hours,
            ["customers_affected"] = customers,
            ["is_safety_hazard"] = hazard,
            ["meter_start"] = (decimal?)meterStart,
            ["meter_end"] = (decimal?)meterEnd,
        };

        var values = FormComputedColumnEvaluator.Evaluate(schema, answers).ToDictionary(v => v.Key);

        values["severity"].Text.Should().Be(severity);
        values["water_lost_m3"].Number.Should().Be((decimal)waterLost);
        values["response"].Text.Should().Be(response);
        values["metered_use_m3"].Number.Should().Be((decimal?)meteredUse);
    }

    [Fact]
    public void FulcrumSeed_KeepsTheExportsFieldNames()
    {
        var schema = FormSchemaParser.Parse(ReadResource("fulcrum-field-survey-import.json"));

        // The import maps a column to a field by name alone, so these are a contract with the export.
        schema.Fields.Should().HaveCount(42);
        schema.Fields.Select(f => f.DataName).Should().Contain(["BranchCode", "branch", "title", "address"]);
    }

    [Fact]
    public void DemoSeed_ExercisesEveryInputTypeTheBuilderOffers()
    {
        var schema = FormSchemaParser.Parse(ReadResource("all-input-types.json"));

        var used = schema.Fields.Select(f => f.FieldType).Distinct().ToList();

        // Section is a container, not a leaf field, so it is the one type not expected here.
        var leafTypes = new[]
        {
            FormElementTypes.Text, FormElementTypes.Memo, FormElementTypes.Numeric, FormElementTypes.YesNo,
            FormElementTypes.Date, FormElementTypes.Time, FormElementTypes.CalendarWithHours,
            FormElementTypes.DateTime, FormElementTypes.SingleChoice, FormElementTypes.MultipleChoice,
            FormElementTypes.Signature, FormElementTypes.Photo, FormElementTypes.Video, FormElementTypes.Audio,
            FormElementTypes.File, FormElementTypes.Barcode, FormElementTypes.Geolocation,
        };

        used.Should().BeEquivalentTo(leafTypes);
    }

    [Fact]
    public void DemoSeed_CarriesTheRulesThatMakeItWorthFilling()
    {
        var schema = FormSchemaParser.Parse(ReadResource("all-input-types.json"));

        schema.Fields.Should().Contain(f => f.AllowOther, "a choice offering Other exercises the companion column");
        schema.Fields.Should().Contain(f => f.Rules!.VisibleConditions.Count > 0, "visibility rules must be exercised");
        schema.Fields.Should().Contain(f => FormRuleEngine.HasConditions(f.Rules!.RequiredConditions));
        schema.Fields.Where(f => f.Rules!.Pattern != null).Should().NotBeEmpty("a regex pattern must be exercised");
        schema.Fields.Where(f => f.DateConstraint != null).Should().NotBeEmpty("a date rule must be exercised");
    }

    [Fact]
    public void TheTwoSeeds_DoNotClaimTheSameNameUnderDifferentTypes()
    {
        var fulcrum = FormWritableFields.Of(FormSchemaParser.Parse(ReadResource("fulcrum-field-survey-import.json")))
            .ToDictionary(f => f.Name, f => f.FieldType, StringComparer.OrdinalIgnoreCase);

        var demo = FormWritableFields.Of(FormSchemaParser.Parse(ReadResource("all-input-types.json")));

        // Both are published into one shared catalog, so a clash would fail the second seed.
        demo.Where(f => fulcrum.ContainsKey(f.Name))
            .Should().OnlyContain(f => fulcrum[f.Name] == f.FieldType);
    }
}
