namespace NWFM.Tests.Modules.Workflow;

using FluentAssertions;
using global::Workflow.Application.Settings;

public sealed class WorkflowSettingsTests
{
    [Fact]
    public void IsEnabled_Default_IsFalse()
        => new WorkflowSettings().IsEnabled.Should().BeFalse();

    [Fact]
    public void SectionName_IsWorkflowSettings()
        => WorkflowSettings.SectionName.Should().Be("WorkflowSettings");

    [Fact]
    public void IsEnabled_CanBeOverriddenToTrue()
        => new WorkflowSettings { IsEnabled = true }.IsEnabled.Should().BeTrue();
}
