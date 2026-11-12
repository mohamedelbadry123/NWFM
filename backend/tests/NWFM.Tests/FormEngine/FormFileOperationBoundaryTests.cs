using FormEngine.Application.Common.Schema;
using FormEngine.Application.Uploads.Commands.DeleteFormFile;
using FormEngine.Application.Uploads.Commands.UploadFormFile;
using FormEngine.Application.Uploads.Queries.GetFormFile;
using FormEngine.Domain.Constants;
using FormEngine.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;
using NWFM.Shared.Abstractions;
using NWFM.Shared.Constants;
using NWFM.Shared.Options;
using NWFM.Shared.Storage;

namespace NWFM.Tests.Modules.FormEngine;

public sealed class FormFileOperationBoundaryTests
{
    [Theory]
    [InlineData("owner", false, true, true)]
    [InlineData("other", false, false, false)]
    [InlineData("reviewer", false, false, false)]
    [InlineData("reviewer", true, true, false)]
    [InlineData("owner", true, true, false)]
    [InlineData("admin", false, true, true)]
    [InlineData("admin", true, true, false)]
    public async Task ReadAndDeleteAuthorizeTheExactFileAndOperationBeforeTouchingBytes(string actor, bool submitted, bool read, bool delete)
    {
        using var db = FormEngineTestData.CreateContext();
        var file = SubmissionFile.CreatePending(Guid.NewGuid(), Guid.NewGuid(), 1, "photos", "file.png", "image/png", 1,
            "pending/exact-object.png", null, null, "owner");
        if (submitted) file.LinkTo(Guid.NewGuid(), "forms/exact-object.png", null, null, DateTime.UtcNow);
        db.Add(file); await db.SaveChangesAsync();
        var user = new Mock<ICurrentUser>(); user.SetupGet(u => u.Id).Returns(actor);
        user.Setup(u => u.IsInRole(NwfmRoles.Administrator)).Returns(actor == "admin");
        user.Setup(u => u.HasPermission(NwfmPolicies.ViewSubmissions)).Returns(actor == "reviewer");
        var storage = new Mock<IFileStorage>(MockBehavior.Strict);
        storage.Setup(s => s.OpenReadAsync(file.RelativePath, It.IsAny<CancellationToken>())).ReturnsAsync(() => new MemoryStream([1]));
        storage.Setup(s => s.DeleteAsync(file.RelativePath, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var result = await new GetFormFileQueryHandler(db, storage.Object, user.Object).Handle(new(file.Id), default);
        Assert.Equal(read, result.IsSuccess); if (result.IsSuccess) await result.Value!.Content.DisposeAsync();
        var removed = await new DeleteFormFileCommandHandler(db, storage.Object, user.Object, TimeProvider.System).Handle(new(file.Id), default);
        Assert.Equal(delete, removed.IsSuccess);
        storage.Verify(s => s.OpenReadAsync(file.RelativePath, It.IsAny<CancellationToken>()), Times.Exactly(read ? 1 : 0));
        storage.Verify(s => s.DeleteAsync(file.RelativePath, It.IsAny<CancellationToken>()), Times.Exactly(delete ? 1 : 0));
        storage.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task MigratedPendingReferenceDeletionNeverDeletesSharedArchiveBytes()
    {
        using var db = FormEngineTestData.CreateContext();
        var file = SubmissionFile.CreateMigrated(Guid.NewGuid(), Guid.NewGuid(), 1, "photos", "archive.png", "image/png", 1,
            "archive/shared.png", null, null, "owner");
        db.Add(file); await db.SaveChangesAsync();
        var storage = new Mock<IFileStorage>(MockBehavior.Strict);
        var user = Mock.Of<ICurrentUser>(u => u.Id == "owner");
        Assert.True((await new DeleteFormFileCommandHandler(db, storage.Object, user, TimeProvider.System).Handle(new(file.Id), default)).IsSuccess);
        Assert.False((await db.SubmissionFiles.SingleAsync()).IsActive); storage.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task UploadResolvesExactPublishedVersionAndMissingVersionNeverWrites()
    {
        using var db = FormEngineTestData.CreateContext();
        var form = FormDefinition.Create("MEDIA", "Media", "Media", FormCategories.General, null, null, "owner", DateTime.UtcNow);
        form.SetSchema(FormEngineTestData.SimpleSchema("photos", FormElementTypes.Photo), null, null, "owner", DateTime.UtcNow);
        form.Publish("owner", [new(FormTargetClients.Formly, form.SchemaJson, "{}")], DateTime.UtcNow);
        db.Add(form);
        await db.SaveChangesAsync();
        var storage = new Mock<IFileStorage>(MockBehavior.Strict);
        storage.Setup(s => s.SaveAsync(It.IsAny<Stream>(), It.IsAny<string>(), "image/png", "pending", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Stream _, string name, string _, string _, CancellationToken _) => new StoredFile("pending/" + name, 1));
        var handler = new UploadFormFileCommandHandler(db, storage.Object, Mock.Of<ICurrentUser>(u => u.Id == "owner"), Options.Create(new FileStorageOptions()));
        var request = new UploadFormFileCommand { FormDefinitionId = form.Id, VersionNo = 2, DataName = "photos", FileName = "file.png", ContentType = "image/png", SizeBytes = 1, Content = new MemoryStream([1]) };
        Assert.True((await handler.Handle(request, default)).IsFailure); storage.VerifyNoOtherCalls();
        Assert.True((await handler.Handle(request with { VersionNo = 1 }, default)).IsSuccess);
        var file = await db.SubmissionFiles.SingleAsync(); Assert.Equal(1, file.FormVersionNo); Assert.Equal("owner", file.UploadedBy);
        Assert.Equal("pending/" + file.Id.ToString("N") + ".png", file.RelativePath);
    }
}
