namespace NWFM.Tests.Modules.Workflow;

using FluentAssertions;
using global::Workflow.Domain.Entities;
using global::Workflow.Domain.Enums;

public sealed class WorkflowVariableTests
{
    [Fact]
    public void Create_StoresAllFields()
    {
        var instanceId = Guid.NewGuid();
        var orgId      = Guid.NewGuid();
        var now        = DateTime.UtcNow;

        var v = WorkflowVariable.Create(orgId, instanceId, "Status", VariableDataType.String, "\"Approved\"", now);

        v.OrganizationId.Should().Be(orgId);
        v.WorkflowInstanceId.Should().Be(instanceId);
        v.VariableName.Should().Be("Status");
        v.ValueJson.Should().Be("\"Approved\"");
        v.DataType.Should().Be(VariableDataType.String);
    }

    [Fact]
    public void SetValue_UpdatesValueJson()
    {
        var v = WorkflowVariable.Create(
            Guid.NewGuid(), Guid.NewGuid(), "Count", VariableDataType.Integer, "0", DateTime.UtcNow);
        v.SetValue("42", DateTime.UtcNow.AddSeconds(1));
        v.ValueJson.Should().Be("42");
    }

    [Fact]
    public void Create_WithNullValue_IsAllowed()
    {
        var v = WorkflowVariable.Create(
            Guid.NewGuid(), Guid.NewGuid(), "Optional", VariableDataType.String, null, DateTime.UtcNow);
        v.ValueJson.Should().BeNull();
    }
}
