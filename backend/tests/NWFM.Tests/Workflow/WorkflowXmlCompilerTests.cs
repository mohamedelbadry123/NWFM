namespace NWFM.Tests.Modules.Workflow;

using FluentAssertions;
using global::Workflow.Infrastructure.Services;

public sealed class WorkflowXmlCompilerTests
{
    private readonly WorkflowXmlCompiler _compiler = new();

    private const string ValidXml = """
        <Workflow xmlns="https://privora.io/workflow/v1">
          <Activities>
            <Activity nodeKey="start" type="Start" name="Start" />
            <Activity nodeKey="task1" type="UserTask" name="Review">
              <AssignmentRule assigneeType="AssignmentGroup" referenceId="00000000-0000-0000-0000-000000000001" priority="1" isFallback="false" />
            </Activity>
            <Activity nodeKey="end" type="End" name="End" />
          </Activities>
          <Transitions>
            <Transition key="t1" from="start" to="task1" priority="1" />
            <Transition key="t2" from="task1" to="end" priority="1" />
          </Transitions>
        </Workflow>
        """;

    [Fact]
    public void Compile_WithValidXml_ReturnsSuccess()
    {
        var result = _compiler.Compile(ValidXml, out var hash);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Activities.Should().HaveCount(3);
        result.Value.Transitions.Should().HaveCount(2);
        hash.Should().NotBeNullOrEmpty();
        hash.Should().HaveLength(64);
    }

    [Fact]
    public void Compile_WithValidXml_ParsesActivityTypes()
    {
        var result = _compiler.Compile(ValidXml, out _);

        var start = result.Value!.Activities.First(a => a.NodeKey == "start");
        start.ActivityTypeName.Should().Be("Start");

        var task = result.Value.Activities.First(a => a.NodeKey == "task1");
        task.ActivityTypeName.Should().Be("UserTask");
        task.AssignmentRules.Should().HaveCount(1);
    }

    [Fact]
    public void Compile_WithValidXml_ParsesTransitions()
    {
        var result = _compiler.Compile(ValidXml, out _);

        var t1 = result.Value!.Transitions.First(t => t.Key == "t1");
        t1.FromNodeKey.Should().Be("start");
        t1.ToNodeKey.Should().Be("task1");
    }

    [Fact]
    public void Compile_WithDtdDeclaration_RejectsXml()
    {
        const string xmlWithDtd = "<!DOCTYPE foo [<!ENTITY x \"y\">]><Workflow xmlns=\"https://privora.io/workflow/v1\"/>";

        var result = _compiler.Compile(xmlWithDtd, out _);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Workflow.Version.DtdDetected");
    }

    [Fact]
    public void Compile_WithEntityDeclaration_RejectsXml()
    {
        const string xmlWithEntity = "<!ENTITY foo \"bar\"><Workflow xmlns=\"https://privora.io/workflow/v1\"/>";

        var result = _compiler.Compile(xmlWithEntity, out _);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Workflow.Version.DtdDetected");
    }

    [Fact]
    public void Compile_WithMalformedXml_ReturnsFailure()
    {
        const string badXml = "<Workflow xmlns=\"https://privora.io/workflow/v1\"><Unclosed>";

        var result = _compiler.Compile(badXml, out _);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Workflow.Version.InvalidXml");
    }

    [Fact]
    public void Compile_WithEmptyContent_ReturnsFailure()
    {
        var result = _compiler.Compile(string.Empty, out _);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Workflow.Version.InvalidXml");
    }

    [Fact]
    public void Compile_WithNullContent_ReturnsFailure()
    {
        var result = _compiler.Compile(null!, out _);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void Compile_SameXmlTwice_ProducesSameHash()
    {
        _compiler.Compile(ValidXml, out var hash1);
        _compiler.Compile(ValidXml, out var hash2);

        hash1.Should().Be(hash2);
    }

    [Fact]
    public void Compile_WithVariables_ParsesVariables()
    {
        const string xmlWithVars = """
            <Workflow xmlns="https://privora.io/workflow/v1">
              <Activities>
                <Activity nodeKey="start" type="Start" name="Start" />
                <Activity nodeKey="end" type="End" name="End" />
              </Activities>
              <Transitions>
                <Transition key="t1" from="start" to="end" priority="1" />
              </Transitions>
              <Variables>
                <Variable key="requesterId" name="Requester ID" dataType="Guid" isRequired="true" />
                <Variable key="notes" name="Notes" dataType="String" isSensitive="false" />
              </Variables>
            </Workflow>
            """;

        var result = _compiler.Compile(xmlWithVars, out _);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Variables.Should().HaveCount(2);

        var v = result.Value.Variables.First(x => x.Key == "requesterId");
        v.DataTypeName.Should().Be("Guid");
        v.IsRequired.Should().BeTrue();
    }

    [Fact]
    public void Compile_WithOversizedContent_ReturnsFailure()
    {
        var oversized = new string('x', 600 * 1024);

        var result = _compiler.Compile(oversized, out _);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Workflow.Version.XmlTooLarge");
    }

    [Fact]
    public void Compile_WithManySelfClosingActivities_DoesNotTreatSiblingsAsNested()
    {
        var activities = string.Join(Environment.NewLine,
            Enumerable.Range(1, 20).Select(i =>
                $"<Activity nodeKey=\"node-{i}\" type=\"{(i == 1 ? "Start" : i == 20 ? "End" : "ScriptTask")}\" name=\"Node {i}\" />"));
        var transitions = string.Join(Environment.NewLine,
            Enumerable.Range(1, 19).Select(i =>
                $"<Transition key=\"edge-{i}\" from=\"node-{i}\" to=\"node-{i + 1}\" />"));
        var xml = $"<Workflow xmlns=\"https://privora.io/workflow/v1\"><Activities>{activities}</Activities><Transitions>{transitions}</Transitions></Workflow>";

        var result = _compiler.Compile(xml, out _);

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Message : "valid sibling depth");
        result.Value!.Activities.Should().HaveCount(20);
        result.Value.Transitions.Should().HaveCount(19);
    }

    [Fact]
    public void Compile_WithLegacyNamespace_ReturnsSuccess()
    {
        const string legacyXml = """
            <WorkflowDefinition xmlns="urn:privora:workflow:v1">
              <Activities>
                <Activity nodeKey="start" type="Start" name="Start" />
                <Activity nodeKey="end" type="End" name="End" />
              </Activities>
              <Transitions>
                <Transition key="t1" from="start" to="end" priority="1" />
              </Transitions>
            </WorkflowDefinition>
            """;

        var result = _compiler.Compile(legacyXml, out var hash);

        result.IsSuccess.Should().BeTrue("legacy namespace urn:privora:workflow:v1 must be accepted for backward-compat");
        result.Value!.Activities.Should().HaveCount(2);
        result.Value.Transitions.Should().HaveCount(1);
        hash.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Compile_WithUnknownNamespace_RejectsXml()
    {
        const string wrongNsXml = """
            <WorkflowDefinition xmlns="https://example.com/unknown">
              <Activities />
              <Transitions />
            </WorkflowDefinition>
            """;

        var result = _compiler.Compile(wrongNsXml, out _);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Workflow.Version.InvalidXml");
        result.Error.Message.Should().Contain("Unexpected namespace");
    }

    [Fact]
    public void Compile_WithOutcomesAndActions_ParsesChildren()
    {
        const string xmlWithChildren = """
            <WorkflowDefinition xmlns="https://privora.io/workflow/v1">
              <Activities>
                <Activity nodeKey="review" type="UserTask" name="Review">
                  <Outcomes>
                    <Outcome key="APPROVE" name="Approve" order="1" isDefault="true" />
                    <Outcome key="REJECT" name="Reject" order="2" requiresComment="true" />
                  </Outcomes>
                  <Actions>
                    <Action key="SEND_EMAIL" trigger="OnComplete" sequence="1" failurePolicy="Continue" />
                  </Actions>
                </Activity>
                <Activity nodeKey="end" type="End" name="End" />
              </Activities>
              <Transitions>
                <Transition key="t1" from="review" to="end" priority="1" />
              </Transitions>
            </WorkflowDefinition>
            """;

        var result = _compiler.Compile(xmlWithChildren, out _);

        result.IsSuccess.Should().BeTrue();
        var review = result.Value!.Activities.First(a => a.NodeKey == "review");
        review.Outcomes.Should().HaveCount(2);
        review.Outcomes.First(o => o.OutcomeKey == "APPROVE").IsDefault.Should().BeTrue();
        review.Outcomes.First(o => o.OutcomeKey == "REJECT").RequiresComment.Should().BeTrue();
        review.Actions.Should().HaveCount(1);
        review.Actions[0].ActionKey.Should().Be("SEND_EMAIL");
        review.Actions[0].ExecutionTriggerName.Should().Be("OnComplete");
    }

    [Fact]
    public void Compile_ActivityLevelAssignmentKey_SynthesizesAssignmentRule()
    {
        const string xml = """
            <Workflow xmlns="https://privora.io/workflow/v1">
              <Activities>
                <Activity nodeKey="start" type="Start" name="Start" />
                <Activity nodeKey="review" type="UserTask" name="Review"
                          assignmentKey="PRIVACY_REVIEW" assignmentPurpose="PrivacyReview" />
                <Activity nodeKey="end" type="End" name="End" />
              </Activities>
              <Transitions>
                <Transition key="t1" from="start" to="review" />
                <Transition key="t2" from="review" to="end" />
              </Transitions>
            </Workflow>
            """;

        var result = _compiler.Compile(xml, out _);

        result.IsSuccess.Should().BeTrue();
        var review = result.Value!.Activities.First(a => a.NodeKey == "review");
        review.AssignmentRules.Should().HaveCount(1);
        review.AssignmentRules[0].AssignmentKey.Should().Be("PRIVACY_REVIEW");
        review.AssignmentRules[0].AssignmentPurpose.Should().Be("PrivacyReview");
        review.AssignmentRules[0].AssigneeTypeName.Should().Be("AssignmentGroup");
    }

    [Fact]
    public void Compile_ActivityLevelAssignmentGroupId_SynthesizesAssignmentRule()
    {
        const string groupId = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";
        var xml = $"""
            <Workflow xmlns="https://privora.io/workflow/v1">
              <Activities>
                <Activity nodeKey="start" type="Start" name="Start" />
                <Activity nodeKey="review" type="UserTask" name="Review"
                          assignmentGroupId="{groupId}" assignmentKey="PRIVACY_REVIEWERS" />
                <Activity nodeKey="end" type="End" name="End" />
              </Activities>
              <Transitions>
                <Transition key="t1" from="start" to="review" />
                <Transition key="t2" from="review" to="end" />
              </Transitions>
            </Workflow>
            """;

        var result = _compiler.Compile(xml, out _);

        result.IsSuccess.Should().BeTrue();
        var review = result.Value!.Activities.First(a => a.NodeKey == "review");
        review.AssignmentGroupId.Should().Be(groupId);
        review.AssignmentRules.Should().HaveCount(1);
        review.AssignmentRules[0].ReferenceId.Should().Be(groupId);
        review.AssignmentRules[0].AssignmentKey.Should().Be("PRIVACY_REVIEWERS");
        review.AssignmentRules[0].AssigneeTypeName.Should().Be("AssignmentGroup");
    }
}
