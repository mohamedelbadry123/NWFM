namespace NWFM.Tests.Modules.Workflow;

using FluentAssertions;
using global::Workflow.Domain.Entities;
using global::Workflow.Domain.Enums;

public sealed class WorkflowVersionTests
{
    private static WorkflowVersion CreateDraft(Guid? defId = null, int number = 1)
        => WorkflowVersion.CreateDraft(
            defId ?? Guid.NewGuid(),
            number,
            Guid.NewGuid(),
            DateTime.UtcNow,
            "Initial draft");

    [Fact]
    public void CreateDraft_SetsStatusToDraft()
    {
        var version = CreateDraft();

        version.Status.Should().Be(WorkflowVersionStatus.Draft);
        version.IsDraft.Should().BeTrue();
        version.ValidationStatus.Should().Be(WorkflowValidationStatus.NotValidated);
        version.SchemaVersion.Should().Be(WorkflowVersion.CurrentSchemaVersion);
    }

    [Fact]
    public void UpdateXml_ResetsValidationStatus()
    {
        var version = CreateDraft();
        version.SetValidationResult(true, "{}", DateTime.UtcNow);
        version.ValidationStatus.Should().Be(WorkflowValidationStatus.Valid);

        version.UpdateXml("<workflow/>", "abc123", DateTime.UtcNow);

        version.ValidationStatus.Should().Be(WorkflowValidationStatus.NotValidated);
        version.XmlContent.Should().Be("<workflow/>");
        version.XmlHash.Should().Be("abc123");
    }

    [Fact]
    public void SetValidationResult_Valid_SetsStatusToValid()
    {
        var version = CreateDraft();
        version.SetValidationResult(true, "{}", DateTime.UtcNow);

        version.ValidationStatus.Should().Be(WorkflowValidationStatus.Valid);
        version.ValidationResultJson.Should().Be("{}");
    }

    [Fact]
    public void SetValidationResult_Invalid_SetsStatusToInvalid()
    {
        var version = CreateDraft();
        version.SetValidationResult(false, "{\"errors\":[]}", DateTime.UtcNow);

        version.ValidationStatus.Should().Be(WorkflowValidationStatus.Invalid);
    }

    [Fact]
    public void Publish_SetsStatusToPublished_AndRecordsPublisher()
    {
        var version = CreateDraft();
        var publisherId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        version.Publish(publisherId, now);

        version.Status.Should().Be(WorkflowVersionStatus.Published);
        version.IsDraft.Should().BeFalse();
        version.PublishedByUserId.Should().Be(publisherId);
        version.PublishedAt.Should().BeCloseTo(now, TimeSpan.FromSeconds(1));
        version.ValidationStatus.Should().Be(WorkflowValidationStatus.Valid);
    }

    [Fact]
    public void Retire_SetsStatusToRetired()
    {
        var version = CreateDraft();
        version.Publish(Guid.NewGuid(), DateTime.UtcNow);

        version.Retire(DateTime.UtcNow);

        version.Status.Should().Be(WorkflowVersionStatus.Retired);
    }

    [Fact]
    public void UpdateDesignerJson_SetsDesignerJson()
    {
        var version = CreateDraft();

        version.UpdateDesignerJson("{\"nodes\":[]}", DateTime.UtcNow);

        version.DesignerJson.Should().Be("{\"nodes\":[]}");
    }

    [Fact]
    public void VersionNumbers_AreSequential()
    {
        var defId = Guid.NewGuid();

        var v1 = WorkflowVersion.CreateDraft(defId, 1, Guid.NewGuid(), DateTime.UtcNow);
        var v2 = WorkflowVersion.CreateDraft(defId, 2, Guid.NewGuid(), DateTime.UtcNow);

        v1.VersionNumber.Should().Be(1);
        v2.VersionNumber.Should().Be(2);
    }

    [Fact]
    public void WorkflowVersion_IsGlobal_NoOrganizationId()
        => typeof(WorkflowVersion)
            .GetProperty("OrganizationId")
            .Should().BeNull("versions follow their global definition, not per-org");
}
