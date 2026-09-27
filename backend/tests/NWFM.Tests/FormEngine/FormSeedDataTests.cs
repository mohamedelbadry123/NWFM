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
