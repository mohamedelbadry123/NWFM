namespace NWFM.Tests.Modules.FormEngine;

using FluentAssertions;
using global::FormEngine.Application.Common.Schema;
using global::FormEngine.Domain.Constants;

public sealed class FormSchemaParserTests
{
    [Fact]
    public void Parse_FlattensSections_AndDropsTheSectionItself()
    {
        var schema = FormSchemaParser.Parse(FormEngineTestData.SectionedSchema);

        schema.NameEn.Should().Be("Inspection");
        schema.Fields.Select(f => f.DataName)
            .Should().BeEquivalentTo(["inspector_name", "pipe_material", "is_open", "depth_m"]);
    }

    [Fact]
    public void Parse_CarriesSectionVisibilityOntoEachChild()
    {
        var schema = FormSchemaParser.Parse(FormEngineTestData.SectionedSchema);

        var inSection = schema.Fields.First(f => f.DataName == "inspector_name");
        var outsideSection = schema.Fields.First(f => f.DataName == "depth_m");

        // A field inside a conditional section is only on screen when the section is.
        inSection.Rules!.VisibleConditions.Should().HaveCount(1);
        outsideSection.Rules!.VisibleConditions.Should().BeEmpty();
    }

    [Fact]
    public void Parse_TrimsDataNames_SoTheyMatchTheColumnTheyBecome()
    {
        const string json = """
        { "elements": [ { "type": "text", "data_name": " leak_type " } ] }
        """;

        FormSchemaParser.Parse(json).Fields.Single().DataName.Should().Be("leak_type");
    }

    [Fact]
    public void Parse_ReadsChoicesAndAllowOther()
    {
        var field = FormSchemaParser.Parse(FormEngineTestData.SectionedSchema)
            .Fields.First(f => f.DataName == "pipe_material");

        field.AllowOther.Should().BeTrue();
        field.Choices.Select(c => c.Value).Should().BeEquivalentTo(["steel", "pvc"]);
    }

    [Fact]
    public void Parse_IgnoresPropertiesThisServerDoesNotKnow()
    {
        // A schema written by another product carries extra keys; they must not break the parse.
        const string json = """
        { "elements": [ { "type": "text", "data_name": "note", "c2m_parameter_name": "X", "unknown": { "a": 1 } } ] }
        """;

        FormSchemaParser.Parse(json).Fields.Single().DataName.Should().Be("note");
    }

    [Theory]
    [InlineData("""{ "elements": [] }""", false)]
    [InlineData("""{ "elements": [ { "type": "text", "data_name": "a" } ] }""", true)]
    [InlineData("{}", false)]
    [InlineData("not json", false)]
    public void HasElements_TellsWhetherThereIsAnythingToPublish(string json, bool expected) =>
        FormSchemaParser.HasElements(json).Should().Be(expected);

    [Fact]
    public void Parse_ReadsDateConstraint_AndDropsAnUnknownRule()
    {
        const string json = """
        { "elements": [
            { "type": "date", "data_name": "seen_on", "date_rule": "on_or_before", "min_date": "2026-01-01" },
            { "type": "date", "data_name": "other_on", "date_rule": "sometime_soon" } ] }
        """;

        var fields = FormSchemaParser.Parse(json).Fields;

        var known = fields.First(f => f.DataName == "seen_on");
        known.DateConstraint!.Rule.Should().Be(FormDateRules.OnOrBefore);
        known.DateConstraint.MinDate.Should().Be(new DateOnly(2026, 1, 1));

        // An unrecognised rule relaxes rather than rejects.
        fields.First(f => f.DataName == "other_on").DateConstraint.Should().BeNull();
    }

    [Fact]
    public void Parse_NormalizesAllowedExtensions()
    {
        const string json = """
        { "elements": [ { "type": "file", "data_name": "docs", "allowed_extensions": [".PDF", " pdf ", "docx"] } ] }
        """;

        FormSchemaParser.Parse(json).Fields.Single().AllowedExtensions
            .Should().BeEquivalentTo(["pdf", "docx"]);
    }
}
