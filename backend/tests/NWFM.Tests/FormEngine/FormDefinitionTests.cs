namespace NWFM.Tests.Modules.FormEngine;

using FluentAssertions;
using global::FormEngine.Domain.Constants;
using global::FormEngine.Domain.Entities;
using NWFM.Shared.Exceptions;

public sealed class FormDefinitionTests
{
    private static readonly DateTime Now = new(2026, 9, 20, 8, 0, 0, DateTimeKind.Utc);

    private static FormDefinition NewForm(string code = "FRM-001") =>
        FormDefinition.Create(code, "Leak Report", "تقرير تسرب", FormCategories.Inspection, "D-1", "tester", Now);

    private static FormVersionSnapshot[] Snapshot(FormDefinition form) =>
        [new(FormTargetClients.Formly, form.SchemaJson, "{}")];

    [Fact]
    public void Create_StartsAsADraftThatCannotYetBeFilled()
    {
        var form = NewForm();

        form.Status.Should().Be(FormStatuses.Draft);
        form.CurrentVersionNo.Should().BeNull();
        form.AcceptsSubmissions.Should().BeFalse();
        form.IsActive.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_RefusesAFormWithoutACode(string code) =>
        FluentActions.Invoking(() => NewForm(code)).Should().Throw<DomainException>();

    [Fact]
    public void Create_RefusesAnUnknownCategory() =>
        FluentActions
            .Invoking(() => FormDefinition.Create("C", "En", "Ar", "NOT_A_CATEGORY", null, null, Now))
            .Should().Throw<DomainException>();

    [Fact]
    public void Publish_FreezesAVersionAndNumbersItFromOne()
    {
        var form = NewForm();
        form.SetSchema(FormEngineTestData.SimpleSchema(), null, null, "tester", Now);

        var version = form.Publish("tester", Snapshot(form), Now);

        form.Status.Should().Be(FormStatuses.Published);
        form.CurrentVersionNo.Should().Be(1);
        form.AcceptsSubmissions.Should().BeTrue();
        version.VersionNo.Should().Be(1);
        version.TargetClient.Should().Be(FormTargetClients.Formly);
        form.Versions.Should().ContainSingle();
    }

    [Fact]
    public void Publish_RefusesAFormWithNoSchema() =>
        FluentActions.Invoking(() => NewForm().Publish("tester", [new(FormTargetClients.Formly, "{}", "{}")], Now))
            .Should().Throw<DomainException>();

    [Fact]
    public void SetSchema_OnAPublishedForm_ReopensADraftButKeepsItFillable()
    {
        var form = NewForm();
        form.SetSchema(FormEngineTestData.SimpleSchema(), null, null, "tester", Now);
        form.Publish("tester", Snapshot(form), Now);

        form.SetSchema(FormEngineTestData.SimpleSchema("depth_m"), null, null, "tester", Now);

        form.Status.Should().Be(FormStatuses.Draft);
        // The published version is untouched, so work pinned to it keeps working.
        form.CurrentVersionNo.Should().Be(1);
        form.AcceptsSubmissions.Should().BeTrue();
    }

    [Fact]
    public void SetSchema_SyncsTheNamesTheBuilderCarries()
    {
        var form = NewForm();

        form.SetSchema(FormEngineTestData.SimpleSchema(), "New English", "اسم جديد", "tester", Now);

        form.NameEn.Should().Be("New English");
        form.NameAr.Should().Be("اسم جديد");
    }

    [Fact]
    public void SetSchema_ClipsANameTooLongForItsColumn()
    {
        var form = NewForm();

        form.SetSchema(FormEngineTestData.SimpleSchema(), new string('x', 400), null, "tester", Now);

        form.NameEn.Length.Should().Be(FormDefinition.NameMaxLength);
    }

    [Fact]
    public void PublishTwice_IncrementsTheVersion()
    {
        var form = NewForm();
        form.SetSchema(FormEngineTestData.SimpleSchema(), null, null, "tester", Now);
        form.Publish("tester", Snapshot(form), Now);

        form.SetSchema(FormEngineTestData.SimpleSchema("depth_m"), null, null, "tester", Now);
        var second = form.Publish("tester", Snapshot(form), Now);

        second.VersionNo.Should().Be(2);
        form.CurrentVersionNo.Should().Be(2);
        form.Versions.Should().HaveCount(2);
    }

    [Fact]
    public void Deprecate_OnlyFromPublished_AndThenNoMoreSubmissions()
    {
        var draft = NewForm();
        FluentActions.Invoking(() => draft.Deprecate("tester", Now)).Should().Throw<DomainException>();

        draft.SetSchema(FormEngineTestData.SimpleSchema(), null, null, "tester", Now);
        draft.Publish("tester", Snapshot(draft), Now);
        draft.Deprecate("tester", Now);

        draft.Status.Should().Be(FormStatuses.Deprecated);
        draft.AcceptsSubmissions.Should().BeFalse();
    }

    [Fact]
    public void Archive_DeactivatesTheFormAndFreezesIt()
    {
        var form = NewForm();
        form.Archive("tester", Now);

        form.Status.Should().Be(FormStatuses.Archived);
        form.IsActive.Should().BeFalse();

        FluentActions.Invoking(() => form.SetSchema("{}", null, null, "tester", Now)).Should().Throw<DomainException>();
        FluentActions.Invoking(() => form.UpdateDetails("a", "b", FormCategories.General, null, "tester", Now))
            .Should().Throw<DomainException>();
        FluentActions.Invoking(() => form.Archive("tester", Now)).Should().Throw<DomainException>();
    }

    [Fact]
    public void Clone_CopiesTheSchemaAsAFreshDraftWithNoHistory()
    {
        var form = NewForm();
        form.SetSchema(FormEngineTestData.SimpleSchema(), null, null, "tester", Now);
        form.Publish("tester", Snapshot(form), Now);

        var clone = form.Clone("FRM-002", "Copy", "نسخة", "tester", Now);

        clone.Code.Should().Be("FRM-002");
        clone.SchemaJson.Should().Be(form.SchemaJson);
        clone.Status.Should().Be(FormStatuses.Draft);
        clone.CurrentVersionNo.Should().BeNull();
        clone.Versions.Should().BeEmpty();
    }
}
