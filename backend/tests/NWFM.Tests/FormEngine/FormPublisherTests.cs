namespace NWFM.Tests.Modules.FormEngine;

using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using global::FormEngine.Application.Common.Interfaces;
using global::FormEngine.Application.Common.Schema;
using global::FormEngine.Application.Constants;
using global::FormEngine.Application.Forms.Common;
using global::FormEngine.Domain.Constants;
using global::FormEngine.Domain.Entities;
using global::FormEngine.Infrastructure.Persistence;
using NWFM.Shared.Caching;

public sealed class FormPublisherTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 9, 20, 8, 0, 0, DateTimeKind.Utc);

    private readonly FormEngineDbContext _context = FormEngineTestData.CreateContext();
    private readonly Mock<IFormSubmissionStore> _store = new();
    private readonly Mock<ICacheService> _cache = new();
    private readonly FormPublisher _publisher;

    public FormPublisherTests()
    {
        _publisher = new FormPublisher(
            _context,
            _store.Object,
            _cache.Object,
            new FakeTimeProvider(Now));
    }

    public void Dispose() => _context.Dispose();

    private async Task<FormDefinition> AddFormAsync(string schemaJson, string code = "FRM-001")
    {
        var form = FormDefinition.Create(code, "Leak", "تسرب", FormCategories.Inspection, null, "tester", Now);
        form.SetSchema(schemaJson, null, null, "tester", Now);

        _context.FormDefinitions.Add(form);
        await _context.SaveChangesAsync();

        return form;
    }

    private Task<List<FormField>> FieldsOf(FormDefinition form) =>
        _context.FormFields.Where(f => f.FormDefinitionId == form.Id).ToListAsync();

    [Fact]
    public async Task Publish_RegistersEveryDataNameAndItsCompanionOnTheForm()
    {
        var form = await AddFormAsync(FormEngineTestData.SectionedSchema);

        var result = await _publisher.PublishAsync(form.Id, "tester", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();

        var fields = await FieldsOf(form);
        fields.Select(c => c.DataName).Should().BeEquivalentTo(
            ["inspector_name", "pipe_material", "pipe_material_other", "is_open", "depth_m"]);

        // The companion holds typed free text, whatever its owner's type is.
        var companion = fields.Single(c => c.DataName == "pipe_material_other");
        companion.FieldType.Should().Be(FormElementTypes.Text);
        companion.IsCompanion.Should().BeTrue();
        companion.LabelEn.Should().EndWith("(other)");

        fields.Should().OnlyContain(f => f.FirstVersionNo == 1 && f.LastVersionNo == 1);
    }

    [Fact]
    public async Task Publish_PersistsTheFrozenVersionRow()
    {
        var form = await AddFormAsync(FormEngineTestData.SimpleSchema());

        await _publisher.PublishAsync(form.Id, "tester", CancellationToken.None);

        // The version is reached only through the aggregate's collection, so this is what proves EF
        // inserts it rather than treating it as a row it has already seen.
        var stored = await _context.FormVersions.SingleAsync(v => v.FormDefinitionId == form.Id);

        stored.VersionNo.Should().Be(1);
        stored.TargetClient.Should().Be(FormTargetClients.Formly);
        stored.PublishedBy.Should().Be("tester");
        stored.SchemaJson.Should().Contain("meter_reading");
        stored.SnapshotJson.Should().Contain("FRM-001");
    }

    [Fact]
    public async Task Publish_NamesTheFormsOwnTableAfterItsCode_AndBuildsIt()
    {
        var form = await AddFormAsync(FormEngineTestData.SimpleSchema());
        FormTable? built = null;
        _store.Setup(s => s.EnsureFormTableAsync(It.IsAny<FormTable>(), It.IsAny<CancellationToken>()))
            .Callback<FormTable, CancellationToken>((table, _) => built = table);

        await _publisher.PublishAsync(form.Id, "tester", CancellationToken.None);

        form.SubmissionTable.Should().Be("SUB_FRM_001");

        built.Should().NotBeNull();
        built!.TableName.Should().Be("SUB_FRM_001");
        built.FieldTypes.Should().ContainKey("meter_reading").WhoseValue.Should().Be(FormElementTypes.Numeric);

        // The lock is this form's alone, plus the one naming a table for the first time.
        _store.Verify(s => s.AcquireLockAsync(FormStorageLocks.Form(form.Id), It.IsAny<CancellationToken>()), Times.Once);
        _store.Verify(s => s.AcquireLockAsync(FormStorageLocks.TableNaming, It.IsAny<CancellationToken>()), Times.Once);
        _cache.Verify(c => c.RemoveAsync(NWFM.Shared.Constants.CacheKeys.FormEngine.FieldCatalog, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Republish_KeepsTheTable_AndAddsOnlyTheNewColumns()
    {
        var form = await AddFormAsync(FormEngineTestData.SimpleSchema());
        await _publisher.PublishAsync(form.Id, "tester", CancellationToken.None);

        form.SetSchema(FormEngineTestData.TwoFieldSchema, null, null, "tester", Now);
        await _context.SaveChangesAsync();
        _store.Invocations.Clear();

        var result = await _publisher.PublishAsync(form.Id, "tester", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        form.CurrentVersionNo.Should().Be(2);
        form.SubmissionTable.Should().Be("SUB_FRM_001");

        var fields = await FieldsOf(form);
        fields.Single(f => f.DataName == "meter_reading").Should().Match<FormField>(f => f.FirstVersionNo == 1 && f.LastVersionNo == 2);
        fields.Single(f => f.DataName == "depth_m").FirstVersionNo.Should().Be(2);

        // A table already named is not named again.
        _store.Verify(s => s.AcquireLockAsync(FormStorageLocks.TableNaming, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Republish_ChangingAFieldsType_IsRefusedAndChangesNothing()
    {
        var form = await AddFormAsync(FormEngineTestData.SimpleSchema("address", FormElementTypes.Geolocation));
        await _publisher.PublishAsync(form.Id, "tester", CancellationToken.None);

        form.SetSchema(FormEngineTestData.SimpleSchema("address", FormElementTypes.Text), null, null, "tester", Now);
        await _context.SaveChangesAsync();
        _store.Invocations.Clear();

        var result = await _publisher.PublishAsync(form.Id, "tester", CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(FormEngineErrors.Codes.FieldTypeConflict);
        result.Error.Message.Should().Contain("address");

        // Nothing ran past the check: no new version, and no attempt to touch the table.
        form.CurrentVersionNo.Should().Be(1);
        _store.Verify(s => s.EnsureFormTableAsync(It.IsAny<FormTable>(), It.IsAny<CancellationToken>()), Times.Never);

        // And the change tracker is clean, so a later save cannot smuggle a field in.
        _context.ChangeTracker.Entries<FormField>()
            .Where(e => e.State is EntityState.Added or EntityState.Modified)
            .Should().BeEmpty();
    }

    [Fact]
    public async Task Publish_TheSameNameAsAnotherTypeInAnotherForm_IsAllowed()
    {
        // Each form has its own table, so one form's "address" column says nothing about another's.
        var first = await AddFormAsync(FormEngineTestData.SimpleSchema("address", FormElementTypes.Geolocation));
        await _publisher.PublishAsync(first.Id, "tester", CancellationToken.None);

        var second = await AddFormAsync(FormEngineTestData.SimpleSchema("address", FormElementTypes.Text), "FRM-002");
        var result = await _publisher.PublishAsync(second.Id, "tester", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        (await FieldsOf(first)).Single().FieldType.Should().Be(FormElementTypes.Geolocation);
        (await FieldsOf(second)).Single().FieldType.Should().Be(FormElementTypes.Text);
        second.SubmissionTable.Should().Be("SUB_FRM_002");
    }

    [Fact]
    public async Task Publish_TwoCodesThatCollapseToOneName_GetTablesOfTheirOwn()
    {
        var dashed = await AddFormAsync(FormEngineTestData.SimpleSchema(), "A-B");
        await _publisher.PublishAsync(dashed.Id, "tester", CancellationToken.None);

        var underscored = await AddFormAsync(FormEngineTestData.SimpleSchema(), "A_B");
        await _publisher.PublishAsync(underscored.Id, "tester", CancellationToken.None);

        dashed.SubmissionTable.Should().Be("SUB_A_B");
        underscored.SubmissionTable.Should().Be("SUB_A_B_2");
    }

    [Fact]
    public async Task Publish_RefusesMoreFieldsThanOneTableCanHold()
    {
        var fields = Enumerable.Range(1, FormPublisher.MaxStoredFields + 1)
            .Select(i => $$"""{ "type": "text", "data_name": "field_{{i}}" }""");

        var form = await AddFormAsync($$"""{ "elements": [ {{string.Join(",", fields)}} ] }""");

        var result = await _publisher.PublishAsync(form.Id, "tester", CancellationToken.None);

        result.Error.Code.Should().Be(FormEngineErrors.Codes.SchemaTooManyFields);
        form.CurrentVersionNo.Should().BeNull();
    }

    [Fact]
    public async Task Publish_RefusesADataNameThatCouldNotBeAColumn()
    {
        const string json = """
        { "elements": [ { "type": "text", "data_name": "1bad name" } ] }
        """;

        var form = await AddFormAsync(json);

        var result = await _publisher.PublishAsync(form.Id, "tester", CancellationToken.None);

        result.Error.Code.Should().Be(FormEngineErrors.Codes.SchemaInvalidDataName);
    }

    [Fact]
    public async Task Publish_RefusesTheSameDataNameTwiceInOneForm()
    {
        const string json = """
        { "elements": [ { "type": "text", "data_name": "note" }, { "type": "text", "data_name": "NOTE" } ] }
        """;

        var form = await AddFormAsync(json);

        var result = await _publisher.PublishAsync(form.Id, "tester", CancellationToken.None);

        result.Error.Code.Should().Be(FormEngineErrors.Codes.SchemaDuplicateDataName);
    }

    [Theory]
    [InlineData("SubmittedBy")]
    // Not a column any more, but every read result carries it, so a field may not claim it.
    [InlineData("FormDefinitionId")]
    public async Task Publish_RefusesANameReservedForSubmissionMetadata(string dataName)
    {
        var form = await AddFormAsync(FormEngineTestData.SimpleSchema(dataName, FormElementTypes.Text));

        var result = await _publisher.PublishAsync(form.Id, "tester", CancellationToken.None);

        result.Error.Code.Should().Be(FormEngineErrors.Codes.SchemaReservedDataName);
    }

    [Fact]
    public async Task Publish_RefusesASchemaWithNothingToFill()
    {
        const string emptySections = """
        { "elements": [ { "type": "section", "data_name": "empty", "elements": [] } ] }
        """;

        var form = await AddFormAsync(emptySections);

        var result = await _publisher.PublishAsync(form.Id, "tester", CancellationToken.None);

        result.Error.Code.Should().Be(FormEngineErrors.Codes.SchemaEmpty);
    }

    [Fact]
    public async Task Publish_RefusesAnArchivedForm()
    {
        var form = await AddFormAsync(FormEngineTestData.SimpleSchema());
        form.Archive("tester", Now);
        await _context.SaveChangesAsync();

        var result = await _publisher.PublishAsync(form.Id, "tester", CancellationToken.None);

        result.Error.Code.Should().Be(FormEngineErrors.Codes.FormInvalidStatusTransition);
    }

    [Fact]
    public async Task Publish_AnUnknownFormIsNotFound()
    {
        var result = await _publisher.PublishAsync(Guid.NewGuid(), "tester", CancellationToken.None);

        result.Error.Code.Should().Be(FormEngineErrors.Codes.FormNotFound);
    }

    [Fact]
    public async Task EnsureStorage_RebuildsAFormPublishedBeforeItHadATable()
    {
        // Published the way an older build did: a version, but no table name and no field registry.
        var form = await AddFormAsync(FormEngineTestData.SimpleSchema());
        form.Publish("tester", [new FormVersionSnapshot(FormTargetClients.Formly, form.SchemaJson, "{}")], Now);
        await _context.SaveChangesAsync();

        var result = await _publisher.EnsureStorageAsync(form.Id, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        form.SubmissionTable.Should().Be("SUB_FRM_001");
        (await FieldsOf(form)).Select(f => f.DataName).Should().BeEquivalentTo(["meter_reading"]);
        _store.Verify(s => s.EnsureFormTableAsync(
            It.Is<FormTable>(t => t.TableName == "SUB_FRM_001" && t.FieldTypes.ContainsKey("meter_reading")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task EnsureStorage_IsRepeatable()
    {
        var form = await AddFormAsync(FormEngineTestData.SimpleSchema());
        await _publisher.PublishAsync(form.Id, "tester", CancellationToken.None);

        await _publisher.EnsureStorageAsync(form.Id, CancellationToken.None);
        await _publisher.EnsureStorageAsync(form.Id, CancellationToken.None);

        (await FieldsOf(form)).Should().ContainSingle();
        form.SubmissionTable.Should().Be("SUB_FRM_001");
    }

    [Fact]
    public async Task EnsureStorage_LeavesANeverPublishedFormAlone()
    {
        var form = await AddFormAsync(FormEngineTestData.SimpleSchema());

        var result = await _publisher.EnsureStorageAsync(form.Id, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        form.SubmissionTable.Should().BeNull();
        _store.Verify(s => s.EnsureFormTableAsync(It.IsAny<FormTable>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}

/// <summary>A clock that does not move, so a test can assert on exact timestamps.</summary>
internal sealed class FakeTimeProvider(DateTime utcNow) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => new(utcNow, TimeSpan.Zero);
}
