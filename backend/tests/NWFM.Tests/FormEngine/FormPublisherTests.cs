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

    [Fact]
    public async Task Publish_RegistersEveryDataNameAndItsCompanion()
    {
        var form = await AddFormAsync(FormEngineTestData.SectionedSchema);

        var result = await _publisher.PublishAsync(form.Id, "tester", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();

        var catalog = await _context.FieldCatalog.ToListAsync();
        catalog.Select(c => c.DataName).Should().BeEquivalentTo(
            ["inspector_name", "pipe_material", "pipe_material_other", "is_open", "depth_m"]);

        // The companion holds typed free text, whatever its owner's type is.
        catalog.Single(c => c.DataName == "pipe_material_other").FieldType.Should().Be(FormElementTypes.Text);
        catalog.Single(c => c.DataName == "pipe_material_other").LabelEn.Should().EndWith("(other)");
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
    public async Task Publish_AddsTheColumnsOnce_AndEvictsTheCatalogCache()
    {
        var form = await AddFormAsync(FormEngineTestData.SimpleSchema());

        await _publisher.PublishAsync(form.Id, "tester", CancellationToken.None);

        _store.Verify(s => s.AcquireSchemaLockAsync(It.IsAny<CancellationToken>()), Times.Once);
        _store.Verify(s => s.ReconcileTableAsync(It.IsAny<FormSchema>(), It.IsAny<CancellationToken>()), Times.Once);
        _cache.Verify(c => c.RemoveAsync(NWFM.Shared.Constants.CacheKeys.FormEngine.FieldCatalog, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Publish_ReusingADataNameUnderAnotherType_IsRefusedAndChangesNothing()
    {
        var first = await AddFormAsync(FormEngineTestData.SimpleSchema("address", FormElementTypes.Geolocation));
        await _publisher.PublishAsync(first.Id, "tester", CancellationToken.None);

        var second = await AddFormAsync(FormEngineTestData.SimpleSchema("address", FormElementTypes.Text), "FRM-002");
        _store.Invocations.Clear();

        var result = await _publisher.PublishAsync(second.Id, "tester", CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(FormEngineErrors.Codes.FieldCatalogTypeConflict);
        result.Error.Message.Should().Contain("address");

        // Nothing ran past the check: no version, and no attempt to touch the table.
        second.CurrentVersionNo.Should().BeNull();
        second.Status.Should().Be(FormStatuses.Draft);
        _store.Verify(s => s.ReconcileTableAsync(It.IsAny<FormSchema>(), It.IsAny<CancellationToken>()), Times.Never);

        // And the change tracker is clean, so a later save cannot smuggle the entry in.
        _context.ChangeTracker.Entries<FieldCatalogEntry>()
            .Where(e => e.State == EntityState.Added)
            .Should().BeEmpty();
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

    [Fact]
    public async Task Publish_RefusesANameReservedForSubmissionMetadata()
    {
        var form = await AddFormAsync(FormEngineTestData.SimpleSchema("SubmittedBy", FormElementTypes.Text));

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
    public async Task Publish_ASecondFormReusingANameWithTheSameTypeSharesTheEntry()
    {
        var first = await AddFormAsync(FormEngineTestData.SimpleSchema("depth_m"));
        await _publisher.PublishAsync(first.Id, "tester", CancellationToken.None);

        var second = await AddFormAsync(FormEngineTestData.SimpleSchema("depth_m"), "FRM-002");
        var result = await _publisher.PublishAsync(second.Id, "tester", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        (await _context.FieldCatalog.CountAsync(c => c.DataName == "depth_m")).Should().Be(1);
    }
}

/// <summary>A clock that does not move, so a test can assert on exact timestamps.</summary>
internal sealed class FakeTimeProvider(DateTime utcNow) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => new(utcNow, TimeSpan.Zero);
}
