namespace NWFM.Tests.Modules.Workflow;

using FluentAssertions;
using global::Workflow.Infrastructure.Services;

public sealed class WorkflowTriggerServiceTests
{
    [Fact]
    public void TryGetStartedByUserId_ReadsRequesterUserId()
    {
        var userId = Guid.NewGuid();
        var payload = new Dictionary<string, object?>
        {
            ["RequesterUserId"] = userId,
        };

        WorkflowTriggerService.TryGetStartedByUserId(payload).Should().Be(userId);
    }

    [Fact]
    public void TryGetStartedByUserId_ReadsStringGuid()
    {
        var userId = Guid.NewGuid();
        var payload = new Dictionary<string, object?>
        {
            ["StartedByUserId"] = userId.ToString(),
        };

        WorkflowTriggerService.TryGetStartedByUserId(payload).Should().Be(userId);
    }

    [Fact]
    public void TryGetStartedByUserId_ReturnsNullWhenMissing()
        => WorkflowTriggerService.TryGetStartedByUserId(new Dictionary<string, object?>()).Should().BeNull();
}
