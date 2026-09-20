namespace NWFM.Tests.Modules.FormEngine;

using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using global::FormEngine.Application.Common.Interfaces;
using global::FormEngine.Application.Common.Schema;
using global::FormEngine.Application.Constants;
using global::FormEngine.Application.Submissions.Commands.SubmitForm;
using global::FormEngine.Domain.Constants;
using global::FormEngine.Domain.Entities;
using global::FormEngine.Domain.Options;
using global::FormEngine.Infrastructure.Persistence;
using NWFM.Shared.Abstractions;
using NWFM.Shared.Exceptions;
using NWFM.Shared.Options;
using NWFM.Shared.Storage;

public sealed class SubmitFormCommandHandlerTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 9, 20, 8, 0, 0, DateTimeKind.Utc);

    private readonly FormEngineDbContext _context = FormEngineTestData.CreateContext();
    private readonly Mock<IFormSubmissionStore> _store = new();
    private readonly Mock<IFileStorage> _storage = new();
    private readonly Mock<ICurrentUser> _user = new();
    private readonly SubmitFormCommandHandler _handler;

    public SubmitFormCommandHandlerTests()
    {
        _user.SetupGet(u => u.Id).Returns("user-1");
        _user.SetupGet(u => u.UserName).Returns("Tester");

        _handler = new SubmitFormCommandHandler(
            _context,
            _store.Object,
            _storage.Object,
            _user.Object,
            new FakeTimeProvider(Now),
            Options.Create(new FileStorageOptions()),
            Options.Create(new FormEngineOptions()));
    }

    public void Dispose() => _context.Dispose();

    private async Task<FormDefinition> AddPublishedFormAsync(string? schemaJson = null)
    {
        var json = schemaJson ?? FormEngineTestData.SimpleSchema();
        var form = FormDefinition.Create("FRM-001", "Leak", "تسرب", FormCategories.Inspection, null, "tester", Now);
        form.SetSchema(json, null, null, "tester", Now);
        form.Publish("tester", [new FormVersionSnapshot(FormTargetClients.Formly, json, "{}")], Now);

        _context.FormDefinitions.Add(form);
        await _context.SaveChangesAsync();

        return form;
    }

    private static SubmitFormCommand Command(Guid formId, params (string Key, object? Value)[] answers) => new()
    {
        FormDefinitionId = formId,
        Answers = answers.ToDictionary(a => a.Key, a => a.Value, StringComparer.Ordinal),
    };

    [Fact]
    public async Task Submit_RecordsTheFillAgainstTheCurrentVersion()
    {
        var form = await AddPublishedFormAsync();
        var newId = Guid.NewGuid();
        _store.Setup(s => s.InsertAsync(It.IsAny<FormSubmissionInsert>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(newId);

        var result = await _handler.Handle(Command(form.Id, ("meter_reading", 42)), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.SubmissionId.Should().Be(newId);
        result.Value.VersionNo.Should().Be(1);
        result.Value.IsReplay.Should().BeFalse();

        _store.Verify(s => s.InsertAsync(
            It.Is<FormSubmissionInsert>(i => i.VersionNo == 1 && i.SubmittedBy == "user-1" && i.SubmittedByName == "Tester"),
            It.IsAny<CancellationToken>()));
    }

    [Fact]
    public async Task Submit_AFormThatWasNeverPublishedIsRefused()
    {
        var form = FormDefinition.Create("FRM-002", "Draft", "مسودة", FormCategories.General, null, "tester", Now);
        form.SetSchema(FormEngineTestData.SimpleSchema(), null, null, "tester", Now);
        _context.FormDefinitions.Add(form);
        await _context.SaveChangesAsync();

        var result = await _handler.Handle(Command(form.Id, ("meter_reading", 1)), CancellationToken.None);

        result.Error.Code.Should().Be(FormEngineErrors.Codes.FormNotPublished);
    }

    [Fact]
    public async Task Submit_ADeprecatedFormIsRefused()
    {
        var form = await AddPublishedFormAsync();
        form.Deprecate("tester", Now);
        await _context.SaveChangesAsync();

        var result = await _handler.Handle(Command(form.Id, ("meter_reading", 1)), CancellationToken.None);

        result.Error.Code.Should().Be(FormEngineErrors.Codes.FormNotPublished);
    }

    [Fact]
    public async Task Submit_AFormReopenedAsADraftStillAcceptsFillsAgainstItsPublishedVersion()
    {
        var form = await AddPublishedFormAsync();
        form.SetSchema(FormEngineTestData.SimpleSchema("depth_m"), null, null, "tester", Now);
        await _context.SaveChangesAsync();

        _store.Setup(s => s.InsertAsync(It.IsAny<FormSubmissionInsert>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());

        var result = await _handler.Handle(Command(form.Id, ("meter_reading", 5)), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        form.Status.Should().Be(FormStatuses.Draft);
    }

    [Fact]
    public async Task Submit_AnUnknownVersionIsNotFound()
    {
        var form = await AddPublishedFormAsync();

        var command = Command(form.Id, ("meter_reading", 1)) with { VersionNo = 99 };

        var result = await _handler.Handle(command, CancellationToken.None);

        result.Error.Code.Should().Be(FormEngineErrors.Codes.VersionNotFound);
    }

    [Fact]
    public async Task Submit_AReplayAnswersWithTheOriginalSubmission()
    {
        var form = await AddPublishedFormAsync();
        var clientKey = Guid.NewGuid();
        var originalId = Guid.NewGuid();

        _store.Setup(s => s.FindByClientIdAsync(form.Id, clientKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(originalId);

        var command = Command(form.Id, ("meter_reading", 1)) with { ClientSubmissionId = clientKey };

        var result = await _handler.Handle(command, CancellationToken.None);

        result.Value.SubmissionId.Should().Be(originalId);
        result.Value.IsReplay.Should().BeTrue();
        _store.Verify(s => s.InsertAsync(It.IsAny<FormSubmissionInsert>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Submit_TwoRetriesRacing_TheLoserReportsTheRowTheWinnerWrote()
    {
        var form = await AddPublishedFormAsync();
        var clientKey = Guid.NewGuid();
        var winnerId = Guid.NewGuid();

        _store.SetupSequence(s => s.FindByClientIdAsync(form.Id, clientKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid?)null)
            .ReturnsAsync(winnerId);

        _store.Setup(s => s.InsertAsync(It.IsAny<FormSubmissionInsert>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DuplicateClientSubmissionException(clientKey));

        var command = Command(form.Id, ("meter_reading", 1)) with { ClientSubmissionId = clientKey };

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.SubmissionId.Should().Be(winnerId);
        result.Value.IsReplay.Should().BeTrue();
    }

    [Fact]
    public async Task Submit_TrimsAnswerKeysOntoTheNamesTheSchemaUses()
    {
        var form = await AddPublishedFormAsync();
        FormSubmissionInsert? captured = null;
        _store.Setup(s => s.InsertAsync(It.IsAny<FormSubmissionInsert>(), It.IsAny<CancellationToken>()))
            .Callback<FormSubmissionInsert, CancellationToken>((insert, _) => captured = insert)
            .ReturnsAsync(Guid.NewGuid());

        await _handler.Handle(Command(form.Id, (" meter_reading ", 7)), CancellationToken.None);

        captured!.Answers.Should().ContainKey("meter_reading");
    }

    [Fact]
    public async Task Submit_AggregatesAnswerErrorsIntoOneMessage()
    {
        var form = await AddPublishedFormAsync(FormEngineTestData.SectionedSchema);

        // is_open = yes shows the section, so its required field is genuinely missing.
        var result = await _handler.Handle(Command(form.Id, ("is_open", "yes"), ("depth_m", 99)), CancellationToken.None);

        result.Error.Code.Should().Be(FormEngineErrors.Codes.SubmissionAnswersInvalid);

        // Both problems are reported at once, named by the label the user saw rather than by data name.
        result.Error.Message.Should().Contain("Inspector").And.Contain("Depth");
        _store.Verify(s => s.InsertAsync(It.IsAny<FormSubmissionInsert>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Submit_AnAnswerTheColumnCannotHoldIsReportedAgainstItsField()
    {
        // A geolocation answer passes field validation but can still be a shape the column rejects,
        // which the store reports by naming the field.
        var form = await AddPublishedFormAsync(
            FormEngineTestData.SimpleSchema("site_point", FormElementTypes.Geolocation));

        _store.Setup(s => s.InsertAsync(It.IsAny<FormSubmissionInsert>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DomainException("The answer for 'site_point' is not a valid geolocation value: 'here'."));

        var result = await _handler.Handle(Command(form.Id, ("site_point", "here")), CancellationToken.None);

        result.Error.Code.Should().Be(FormEngineErrors.Codes.SubmissionAnswerRejected);
        result.Error.Message.Should().Contain("site_point");
    }

    [Fact]
    public async Task Submit_ReconcilesTheTableBeforeWriting()
    {
        var form = await AddPublishedFormAsync();
        _store.Setup(s => s.InsertAsync(It.IsAny<FormSubmissionInsert>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());

        await _handler.Handle(Command(form.Id, ("meter_reading", 1)), CancellationToken.None);

        _store.Verify(s => s.ReconcileTableAsync(It.IsAny<global::FormEngine.Application.Common.Schema.FormSchema>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
