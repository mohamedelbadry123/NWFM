namespace NWFM.Tests.Modules.Workflow;

using FluentAssertions;
using global::Workflow.Domain.Enums;

public sealed class ActivityTypePhase10Tests
{
    [Theory]
    [InlineData("InclusiveGateway", ActivityType.InclusiveGateway)]
    [InlineData("CallActivity", ActivityType.CallActivity)]
    [InlineData("ScriptTask", ActivityType.ScriptTask)]
    [InlineData("WaitEvent", ActivityType.WaitEvent)]
    public void TryParse_AcceptsNewActivityTypes(string name, ActivityType expected)
    {
        Enum.TryParse<ActivityType>(name, true, out var parsed).Should().BeTrue();
        parsed.Should().Be(expected);
    }

    [Fact]
    public void EnumValues_MatchPhase10Contract()
    {
        ((int)ActivityType.InclusiveGateway).Should().Be(11);
        ((int)ActivityType.CallActivity).Should().Be(12);
    }
}
