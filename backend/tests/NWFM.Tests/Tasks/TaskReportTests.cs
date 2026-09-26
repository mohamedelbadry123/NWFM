namespace NWFM.Tests.Modules.Tasks;

using System.Text;
using FluentAssertions;
using global::Tasks.Application.Common;
using global::Tasks.Application.Common.Interfaces;
using global::Tasks.Application.Constants;
using global::Tasks.Application.Tasks.Queries.ExportTaskPdf;
using global::Tasks.Domain.Constants;
using global::Tasks.Infrastructure.Persistence;
using global::Tasks.Infrastructure.Reports;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NWFM.Shared.Abstractions;
using NWFM.Shared.Integration.Forms;
using NWFM.Shared.Integration.Organization;
using NWFM.Shared.Organization;
using NWFM.Tests.Modules.FormEngine;

/// <summary>
/// The PDF export: what the handler gathers for the report, and that the renderer turns it into a
/// document even when an upload will not decode.
/// </summary>
public sealed class TaskReportTests : IDisposable
{
    private static readonly OrgHierarchy Hierarchy = OrgHierarchy.Build([("RCBU", "CC")], [("R-16", "RCBU")], []);

    /// <summary>A 1×1 PNG — the smallest image a renderer will accept.</summary>
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    private readonly TasksDbContext _db = TaskTestData.CreateContext();
    private readonly Mock<IOrgScopeProvider> _scopes = new();
    private readonly Mock<IOrgDirectory> _directory = new();
    private readonly Mock<IFormGateway> _forms = new();
    private readonly Mock<ICurrentUser> _user = new();
    private readonly Mock<ITaskReportRenderer> _renderer = new();
    private TaskReport? _rendered;

    public TaskReportTests()
    {
        _scopes.Setup(s => s.GetCurrentUserScopeAsync(It.IsAny<CancellationToken>())).ReturnsAsync(OrgScopeSet.Unrestricted());
        _scopes.Setup(s => s.GetHierarchyAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Hierarchy);
        _directory.Setup(d => d.GetTeamsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, OrgTeamInfo>());
        _directory.Setup(d => d.GetUnitNamesAsync(OrgLevels.Branch, It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, OrgUnitName>(StringComparer.OrdinalIgnoreCase)
            {
                ["R-16"] = new("R-16", "Al Moraba", "المربع"),
            });
        _directory.Setup(d => d.GetUnitNamesAsync(It.Is<string>(l => l != OrgLevels.Branch), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, OrgUnitName>(StringComparer.OrdinalIgnoreCase));
        _forms.Setup(f => f.FindPublishedAsync(TaskTestData.FormId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PublishedFormInfo(TaskTestData.FormId, "FRM", "Survey form", "نموذج المسح", "SURVEY", "PUBLISHED", 1, true));
        _renderer.Setup(r => r.Render(It.IsAny<TaskReport>()))
            .Callback<TaskReport>(report => _rendered = report)
            .Returns([1, 2, 3]);
    }

    public void Dispose() => _db.Dispose();

    private ExportTaskPdfQueryHandler Handler() =>
        new(_db, new TaskAccess(_db, _scopes.Object, _user.Object), _directory.Object, _scopes.Object, _forms.Object, _renderer.Object, new FakeTimeProvider(TaskTestData.Now));

    private async Task<Guid> SeedAsync()
    {
        var type = TaskTestData.Type();
        var task = TaskTestData.Task(type);
        _db.TaskTypes.Add(type);
        _db.Tasks.Add(task);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        return task.Id;
    }

    [Fact]
    public async Task Export_OfATaskOutsideTheCallersTerritory_IsNotFound()
    {
        var taskId = await SeedAsync();
        _scopes.Setup(s => s.GetCurrentUserScopeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OrgScopeSet.FromRows([new OrgScopeRow(OrgLevels.Branch, "J-01", null)], Hierarchy));

        var result = await Handler().Handle(new ExportTaskPdfQuery(taskId, "en"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be(TaskErrors.Codes.TaskNotFound);
        _renderer.Verify(r => r.Render(It.IsAny<TaskReport>()), Times.Never);
    }

    [Fact]
    public async Task Export_PrintsTheLatestFillInTheRequestedLanguage()
    {
        var taskId = await SeedAsync();
        var latestId = Guid.NewGuid();
        var answers = new Dictionary<string, object?> { ["pipe_material"] = "pvc" };

        _forms.Setup(f => f.GetLatestByContextAsync(TaskTestData.FormId, TasksSchema.FormContextType, taskId.ToString("D"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FormSubmissionRecord(latestId, TaskTestData.FormId, 1, "u1", "Sara", TaskTestData.Now, answers));
        _forms.Setup(f => f.DescribeAnswersAsync(TaskTestData.FormId, 1, answers, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new FormAnswerView("pipe_material", "single_choice", "Material", "المادة", "PVC", "بلاستيك", null)]);
        _forms.Setup(f => f.ListFilesByContextAsync(TasksSchema.FormContextType, taskId.ToString("D"), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var result = await Handler().Handle(new ExportTaskPdfQuery(taskId, "ar"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.FileName.Should().Be("Task_TSK-1_20260921.pdf");
        _rendered!.Language.Should().Be("ar");
        _rendered.Answers.Should().ContainSingle().Which.Should().Be(new TaskReportAnswer("المادة", "بلاستيك"));
        _rendered.LatestFill!.FilledBy.Should().Be("Sara");
        _rendered.Branch.Should().Be("R-16 — المربع");
        _rendered.Cluster.Should().Be("CC");
        _rendered.Form.Should().Be("FRM — نموذج المسح");
    }

    [Fact]
    public async Task Export_EmbedsOnlyTheLatestFillsImages()
    {
        var taskId = await SeedAsync();
        var contextId = taskId.ToString("D");
        var latestId = Guid.NewGuid();
        var latestPhoto = Guid.NewGuid();
        var earlierPhoto = Guid.NewGuid();

        _forms.Setup(f => f.GetLatestByContextAsync(TaskTestData.FormId, TasksSchema.FormContextType, contextId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FormSubmissionRecord(latestId, TaskTestData.FormId, 1, "u1", "Sara", TaskTestData.Now, new Dictionary<string, object?>()));
        _forms.Setup(f => f.DescribeAnswersAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<IReadOnlyDictionary<string, object?>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _forms.Setup(f => f.ListFilesByContextAsync(TasksSchema.FormContextType, contextId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new FormFileRecord(latestPhoto, TaskTestData.FormId, latestId, "site_photo", "now.png", "image/png", 70, "LINKED", TaskTestData.Now),
                new FormFileRecord(earlierPhoto, TaskTestData.FormId, Guid.NewGuid(), "site_photo", "before.png", "image/png", 70, "LINKED", TaskTestData.Now.AddDays(-1)),
            ]);
        _forms.Setup(f => f.ReadContextFileAsync(latestPhoto, TasksSchema.FormContextType, contextId, It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Png);

        await Handler().Handle(new ExportTaskPdfQuery(taskId, "en"), CancellationToken.None);

        _forms.Verify(f => f.ReadContextFileAsync(earlierPhoto, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
        _rendered!.Files.Should().HaveCount(2);
        _rendered.Files.Single(f => f.FileName == "now.png").Bytes.Should().Equal(Png);
        _rendered.Files.Single(f => f.FileName == "before.png").Bytes.Should().BeNull();
    }

    [Fact]
    public void Renderer_ProducesAPdfEvenWhenAnUploadWillNotDecode()
    {
        var report = new TaskReport
        {
            Language = "ar",
            GeneratedAt = TaskTestData.Now,
            TaskNumber = "TSK-1",
            Status = TaskStatuses.Returned,
            Priority = "HIGH",
            Source = "MANUAL",
            Notes = "افحص العداد",
            TaskType = "SURVEY — مسح",
            Form = "FRM — نموذج",
            FormVersionNo = 1,
            Cluster = "CC",
            Cbu = "RCBU",
            Branch = "R-16 — المربع",
            OperationArea = "-",
            Department = "-",
            Latitude = 24.66,
            Longitude = 46.71,
            CreatedAt = TaskTestData.Now,
            ReturnReasonCode = "POOR_MEDIA",
            ReturnReason = "الصورة غير واضحة",
            ReturnCount = 1,
            LatestFill = new TaskReportFill("Sara", TaskTestData.Now, 1),
            Answers = [new TaskReportAnswer("المادة", "بلاستيك")],
            Files =
            [
                new TaskReportFile("now.png", "صورة الموقع", "image/png", 70, TaskTestData.Now, false, true, Png),
                new TaskReportFile("broken.jpg", "صورة الموقع", "image/jpeg", 12, TaskTestData.Now, false, true, [0xFF, 0xD8, 0x00]),
                new TaskReportFile("sign.png", "التوقيع", "image/png", 70, TaskTestData.Now, true, true, Png),
            ],
        };

        var pdf = new TaskReportRenderer(NullLogger<TaskReportRenderer>.Instance).Render(report);

        Encoding.ASCII.GetString(pdf, 0, 5).Should().Be("%PDF-");
    }
}
