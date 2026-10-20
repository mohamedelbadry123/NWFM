using System.Text.Json;
using FormEngine.Application.Common.Schema;
using FormEngine.Application.Submissions.Common;
using FormEngine.Domain.Entities;
using Moq;
using NWFM.Shared.Storage;

namespace NWFM.Tests.Modules.FormEngine;

public sealed class SubmissionMediaIsolationTests
{
    [Fact]
    public async Task SameOriginalFileNameUsesDistinctObjectKeys()
    {
        using var db = FormEngineTestData.CreateContext();
        var formId = Guid.NewGuid();
        var first = SubmissionFile.CreatePending(Guid.NewGuid(), formId, 1, "photos", "same.png",
            "image/png", 1, "pending/first.png", null, null, "owner");
        var second = SubmissionFile.CreatePending(Guid.NewGuid(), formId, 1, "photos", "same.png",
            "image/png", 1, "pending/second.png", null, null, "owner");
        db.AddRange(first, second);
        await db.SaveChangesAsync();
        var storage = new Mock<IFileStorage>();
        var names = new List<string>();
        storage.Setup(s => s.MoveAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, string, CancellationToken>((_, _, name, _) => names.Add(name))
            .ReturnsAsync((string _, string folder, string name, CancellationToken _) => folder + "/" + name);
        var schema = FormSchemaParser.Parse(FormEngineTestData.SimpleSchema("photos", FormElementTypes.Photo));
        var answers = new Dictionary<string, object?>
        {
            ["photos"] = JsonSerializer.SerializeToElement(new[] { new { fileId = first.Id }, new { fileId = second.Id } })
        };

        await SubmissionMediaLinker.LinkAsync(db, storage.Object, schema, answers, formId, Guid.NewGuid(),
            "forms/submission", null, null, DateTime.UtcNow, default);

        Assert.Equal(2, names.Distinct().Count());
        Assert.Contains(first.Id.ToString("N") + ".png", names);
        Assert.Contains(second.Id.ToString("N") + ".png", names);
    }
}
