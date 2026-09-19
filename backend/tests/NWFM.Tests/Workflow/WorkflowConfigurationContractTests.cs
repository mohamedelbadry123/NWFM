namespace NWFM.Tests.Modules.Workflow;

using System.Text.Json;
using FluentAssertions;
using global::Workflow.Application.Helpers;

public sealed class WorkflowConfigurationContractTests
{
    [Theory]
    [InlineData("amount >= 100 && approved == true", true)]
    [InlineData("amount < 100 || (approved == true && name == 'review')", true)]
    [InlineData("amount == '150'", false)]
    [InlineData("missing != null", false)]
    [InlineData("amount < 150", false)]
    [InlineData("name == REVIEW", true)]
    public void Conditions_UseTypedComparisonsAndPrecedence(string condition, bool expected)
        => WorkflowCondition.Evaluate(condition, new Dictionary<string, string?>
        { ["amount"] = "150", ["approved"] = "true", ["name"] = "\"review\"" }).Should().Be(expected);

    [Theory]
    [InlineData("amount = 5")]
    [InlineData("process.exit()")]
    [InlineData("amount == 1; drop table data")]
    [InlineData("(amount > 1")]
    public void Conditions_RejectExecutableOrMalformedInput(string expression)
        => WorkflowCondition.IsValid(expression).Should().BeFalse();

    [Theory]
    [InlineData("number", "150", true)]
    [InlineData("number", "\"150\"", false)]
    [InlineData("date", "\"2026-09-19\"", true)]
    [InlineData("date", "\"2026-02-30\"", false)]
    [InlineData("select", "\"Approve\"", true)]
    [InlineData("select", "\"Unknown\"", false)]
    [InlineData("checkbox", "false", true)]
    [InlineData("email", "\"invalid address\"", false)]
    public void TaskForms_EnforceTypesAndSelectOptions(string type, string value, bool valid)
    {
        var config = new WorkflowTaskConfiguration { FormFields = [new("field", "Field", "", type, true, ["Approve", "Reject"])] };
        var error = WorkflowTaskForm.Validate(config, new Dictionary<string, JsonElement> { ["field"] = JsonSerializer.Deserialize<JsonElement>(value) });
        (error is null).Should().Be(valid);
        WorkflowTaskForm.Validate(config, new Dictionary<string, JsonElement>()).Should().Contain("required");
    }
}
