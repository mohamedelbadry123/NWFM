namespace NWFM.Tests.Modules.FormEngine;

using FluentAssertions;
using global::FormEngine.Application.Submissions.Commands.SubmitForm;

/// <summary>
/// A fill's context names what it belongs to. A task's fills are the task module's to record, so the
/// form engine's own endpoint must not accept one posted in a task's name.
/// </summary>
public sealed class SubmitFormContextTests
{
    private static SubmitFormCommand Fill(string? contextType, string? contextId) => new()
    {
        FormDefinitionId = Guid.NewGuid(),
        ContextType = contextType,
        ContextId = contextId,
        Answers = new Dictionary<string, object?> { ["meter_reading"] = 5 },
    };

    [Theory]
    [InlineData("Task")]
    [InlineData(" task ")]
    public void A_fill_posted_for_a_task_is_refused(string contextType)
    {
        var result = new SubmitFormCommandValidator().Validate(Fill(contextType, Guid.NewGuid().ToString()));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(SubmitFormCommand.ContextType));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("WorkItem", "42")]
    public void A_standalone_fill_or_another_context_is_accepted(string? contextType, string? contextId)
    {
        new SubmitFormCommandValidator().Validate(Fill(contextType, contextId)).IsValid.Should().BeTrue();
    }
}
