namespace NWFM.Tests.Modules.FormEngine;

using FluentAssertions;
using global::FormEngine.Application.Common.Schema;

/// <summary>
/// A table name is interpolated into DDL, so what matters is that nothing outside the closed alphabet
/// can reach it — whatever a form code contains.
/// </summary>
public sealed class FormSubmissionTableNameTests
{
    [Theory]
    [InlineData("SRV-FIELD-SURVEY-002", "SUB_SRV_FIELD_SURVEY_002")]
    [InlineData("demo-all-input-types", "SUB_DEMO_ALL_INPUT_TYPES")]
    [InlineData(" leak report ", "SUB_LEAK_REPORT")]
    [InlineData("استبيان", "SUB________")]
    public void For_MapsACodeOntoTheClosedAlphabet(string code, string expected)
    {
        FormSubmissionTableName.For(code).Should().Be(expected);
    }

    [Theory]
    [InlineData("x]; DROP TABLE [FE].[FormDefinitions]; --")]
    [InlineData("a'b\"c")]
    [InlineData("tab\there")]
    public void For_NeverProducesANameThatNeedsEscaping(string code)
    {
        var name = FormSubmissionTableName.For(code);

        FormSubmissionTableName.IsValid(name).Should().BeTrue();
        name.Should().MatchRegex("^SUB_[A-Z0-9_]+$");
    }

    [Fact]
    public void Candidates_NumberTheAlternativesForACollision()
    {
        FormSubmissionTableName.Candidates("A-B").Take(3).Should().Equal("SUB_A_B", "SUB_A_B_2", "SUB_A_B_3");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Submissions")]
    [InlineData("SUB_lower")]
    [InlineData("SUB_A]B")]
    [InlineData("SUB_")]
    public void IsValid_RejectsAnythingThisClassWouldNotHaveProduced(string? name)
    {
        FormSubmissionTableName.IsValid(name).Should().BeFalse();
    }
}
