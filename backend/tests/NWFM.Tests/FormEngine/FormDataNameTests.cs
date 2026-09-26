namespace NWFM.Tests.Modules.FormEngine;

using FluentAssertions;
using global::FormEngine.Application.Common.Schema;
using global::FormEngine.Domain.Constants;

public sealed class FormDataNameTests
{
    [Theory]
    [InlineData("leak_type", true)]
    [InlineData("_private", true)]
    [InlineData("Field1", true)]
    [InlineData("1field", false)]
    [InlineData("leak type", false)]
    [InlineData("leak-type", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsValid_AcceptsOnlyWhatCouldBeASqlColumn(string? name, bool expected) =>
        FormDataName.IsValid(name).Should().Be(expected);

    [Fact]
    public void IsValid_RejectsANameTooLongForAnIdentifier() =>
        FormDataName.IsValid(new string('a', 129)).Should().BeFalse();

    [Fact]
    public void WritableFields_AddsACompanionColumnForAChoiceOfferingOther()
    {
        var schema = FormSchemaParser.Parse(FormEngineTestData.SectionedSchema);

        var writable = FormWritableFields.Of(schema).ToList();

        writable.Should().Contain(f => f.Name == "pipe_material" && f.FieldType == FormElementTypes.SingleChoice);

        // The typed free text needs a column of its own, and it is always text.
        writable.Should().Contain(f => f.Name == "pipe_material_other" && f.FieldType == FormElementTypes.Text);
    }

    [Fact]
    public void WritableFields_DropsANameThatCouldNotBeWritten()
    {
        const string json = """
        { "elements": [ { "type": "text", "data_name": "1bad" }, { "type": "text", "data_name": "good" } ] }
        """;

        FormWritableFields.Of(FormSchemaParser.Parse(json))
            .Select(f => f.Name)
            .Should().BeEquivalentTo(["good"]);
    }

    [Fact]
    public void ChoiceOther_KeyAndOwnerAgreeWithEachOther()
    {
        var schema = FormSchemaParser.Parse(FormEngineTestData.SectionedSchema);
        var fields = schema.Fields.ToDictionary(f => f.DataName, StringComparer.OrdinalIgnoreCase);

        var key = FormChoiceOther.KeyFor("pipe_material");

        key.Should().Be("pipe_material_other");
        FormChoiceOther.OwnerOf(key, fields)!.DataName.Should().Be("pipe_material");
        FormChoiceOther.OwnerOf("depth_m", fields).Should().BeNull();
    }
}
