namespace NWFM.Tests.Modules.Workflow;

using FluentAssertions;
using global::Workflow.Application.Helpers;

public sealed class AssignmentGroupCodeGeneratorTests
{
    [Theory]
    [InlineData("Approval Review", "APPROVAL_REVIEW")]
    [InlineData("  support-team  ", "SUPPORT_TEAM")]
    [InlineData("A", "A")]
    [InlineData("@@@", "GROUP")]
    public void FromName_ProducesStableUppercaseCode(string name, string expected)
        => AssignmentGroupCodeGenerator.FromName(name).Should().Be(expected);

    [Fact]
    public void WithSuffix_AppendsWhenGreaterThanOne()
    {
        AssignmentGroupCodeGenerator.WithSuffix("APPROVAL", 1).Should().Be("APPROVAL");
        AssignmentGroupCodeGenerator.WithSuffix("APPROVAL", 2).Should().Be("APPROVAL_2");
    }
}
