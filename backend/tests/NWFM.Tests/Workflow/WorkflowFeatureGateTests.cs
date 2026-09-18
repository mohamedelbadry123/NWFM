namespace NWFM.Tests.Modules.Workflow;

using FluentAssertions;
using Microsoft.Extensions.Options;
using global::Workflow.Application.Abstractions;
using global::Workflow.Application.Constants;
using global::Workflow.Application.Settings;
using global::Workflow.Infrastructure.Services;

public sealed class WorkflowFeatureGateTests
{
    [Fact]
    public void EnsureEnabled_WhenDisabled_ReturnsFailure()
    {
        IWorkflowFeatureGate gate = BuildGate(isEnabled: false);

        var result = gate.EnsureEnabled();

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void EnsureEnabled_WhenDisabled_ErrorCode_IsModuleDisabled()
    {
        IWorkflowFeatureGate gate = BuildGate(isEnabled: false);

        var result = gate.EnsureEnabled();

        result.Error.Code.Should().Be(WorkflowErrors.ModuleDisabled.Code);
    }

    [Fact]
    public void EnsureEnabled_WhenEnabled_ReturnsSuccess()
    {
        IWorkflowFeatureGate gate = BuildGate(isEnabled: true);

        var result = gate.EnsureEnabled();

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void ModuleDisabledError_Code_MatchesConvention()
        => WorkflowErrors.ModuleDisabled.Code.Should().Be("Workflow.ModuleDisabled");

    private static WorkflowFeatureGate BuildGate(bool isEnabled)
        => new(Options.Create(new WorkflowSettings { IsEnabled = isEnabled }));
}
