namespace NWFM.Tests.Modules.FormEngine;

using FluentAssertions;
using global::FormEngine.Application.Common.Schema;

public sealed class FormAnswerValidatorTests
{
    private static readonly DateTime Now = new(2026, 9, 20, 10, 0, 0, DateTimeKind.Unspecified);

    private static FormSchema Schema(string json) => FormSchemaParser.Parse(json);

    private static Dictionary<string, object?> Answers(params (string Key, object? Value)[] values) =>
        values.ToDictionary(v => v.Key, v => v.Value, StringComparer.Ordinal);

    [Fact]
    public void Validate_RequiredFieldMustBeAnswered()
    {
        var schema = Schema(FormEngineTestData.SectionedSchema);

        // The section's rule holds, so the required field inside it really is on screen.
        var errors = FormAnswerValidator.Validate(schema, Answers(("is_open", "yes")), Now);

        errors.Should().ContainSingle(e => e.DataName == "inspector_name");
    }

    [Fact]
    public void Validate_AFieldHiddenByItsSectionIsNeverRequired()
    {
        var schema = Schema(FormEngineTestData.SectionedSchema);

        // is_open is "no", so the section is not shown — nobody was asked for the inspector's name.
        var errors = FormAnswerValidator.Validate(schema, Answers(("is_open", "no")), Now);

        errors.Should().BeEmpty();
    }

    [Fact]
    public void Validate_ChecksLengthAndPattern()
    {
        var schema = Schema(FormEngineTestData.SectionedSchema);

        var tooShort = FormAnswerValidator.Validate(schema, Answers(("is_open", "yes"), ("inspector_name", "Jo")), Now);
        tooShort.Should().ContainSingle(e => e.DataName == "inspector_name");

        var badPattern = FormAnswerValidator.Validate(schema, Answers(("is_open", "yes"), ("inspector_name", "Jo3hn")), Now);
        badPattern.Should().ContainSingle(e => e.DataName == "inspector_name");

        var good = FormAnswerValidator.Validate(schema, Answers(("is_open", "yes"), ("inspector_name", "John Smith")), Now);
        good.Should().BeEmpty();
    }

    [Fact]
    public void Validate_ChecksNumericBoundsAndIntegerFormat()
    {
        var schema = Schema(FormEngineTestData.SectionedSchema);

        var answers = Answers(("is_open", "no"), ("depth_m", 99));
        FormAnswerValidator.Validate(schema, answers, Now).Should().ContainSingle(e => e.DataName == "depth_m");

        var fractional = Answers(("is_open", "no"), ("depth_m", 2.5));
        FormAnswerValidator.Validate(schema, fractional, Now).Should().ContainSingle(e => e.DataName == "depth_m");

        FormAnswerValidator.Validate(schema, Answers(("is_open", "no"), ("depth_m", 5)), Now).Should().BeEmpty();
    }

    [Fact]
    public void Validate_AcceptsAChoiceValue_AndTheOtherSentinel()
    {
        var schema = Schema(FormEngineTestData.SectionedSchema);

        var known = Answers(("is_open", "no"), ("pipe_material", "steel"));
        FormAnswerValidator.Validate(schema, known, Now).Should().BeEmpty();

        var other = Answers(("is_open", "no"), ("pipe_material", FormChoiceOther.Sentinel));
        FormAnswerValidator.Validate(schema, other, Now).Should().BeEmpty();

        var unknown = Answers(("is_open", "no"), ("pipe_material", "granite"));
        FormAnswerValidator.Validate(schema, unknown, Now).Should().ContainSingle(e => e.DataName == "pipe_material");
    }

    [Fact]
    public void Validate_EnforcesDateRulesEvenWhenFieldRulesAreOff()
    {
        const string json = """
        { "elements": [ { "type": "date", "data_name": "seen_on", "required": true, "date_rule": "on_or_before" } ] }
        """;

        var future = Answers(("seen_on", "2027-01-01"));

        var errors = FormAnswerValidator.Validate(Schema(json), future, Now, enforceFieldRules: false);

        errors.Should().ContainSingle(e => e.DataName == "seen_on");
    }

    [Fact]
    public void Validate_WithFieldRulesOff_LetsAMissingRequiredAnswerThrough()
    {
        var schema = Schema(FormEngineTestData.SectionedSchema);

        FormAnswerValidator
            .Validate(schema, Answers(("is_open", "yes")), Now, enforceFieldRules: false)
            .Should().BeEmpty();
    }

    [Fact]
    public void Validate_RequiredConditionReplacesTheStaticFlag()
    {
        const string json = """
        { "elements": [
            { "type": "yes_no", "data_name": "is_hazard" },
            { "type": "text", "data_name": "asset_tag", "required": false,
              "required_conditions": { "match": "all", "conditions": [ { "field": "is_hazard", "operator": "equal", "value": "yes" } ] } } ] }
        """;

        var schema = Schema(json);

        FormAnswerValidator.Validate(schema, Answers(("is_hazard", "yes")), Now)
            .Should().ContainSingle(e => e.DataName == "asset_tag");

        FormAnswerValidator.Validate(schema, Answers(("is_hazard", "no")), Now).Should().BeEmpty();
    }

    [Fact]
    public void Validate_ADisabledFieldIsNotDemanded()
    {
        // Angular drops a disabled control from the form group, so the browser submits it blank.
        const string json = """
        { "elements": [ { "type": "text", "data_name": "ref_no", "required": true, "disabled": true } ] }
        """;

        FormAnswerValidator.Validate(Schema(json), Answers(), Now).Should().BeEmpty();
    }
}
