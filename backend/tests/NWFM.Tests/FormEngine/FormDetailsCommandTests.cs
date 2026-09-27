namespace NWFM.Tests.Modules.FormEngine;

using FluentAssertions;
using Moq;
using global::FormEngine.Application.Constants;
using global::FormEngine.Application.Forms.Commands.CreateForm;
using global::FormEngine.Application.Forms.Commands.UpdateForm;
using global::FormEngine.Domain.Constants;
using global::FormEngine.Domain.Entities;
using global::FormEngine.Infrastructure.Persistence;
using NWFM.Shared.Abstractions;
using NWFM.Shared.Integration.Organization;

/// <summary>A form's main info names a department and one of that department's field activities.</summary>
public sealed class FormDetailsCommandTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 9, 20, 8, 0, 0, DateTimeKind.Utc);

    private readonly FormEngineDbContext _context = FormEngineTestData.CreateContext();
    private readonly Mock<IOrgDirectory> _directory = new();
    private readonly Mock<ICurrentUser> _user = new();
    private readonly FakeTimeProvider _clock = new(Now);

    public FormDetailsCommandTests()
    {
        _user.SetupGet(u => u.Id).Returns("user-1");

        _directory
            .Setup(d => d.IsFieldActivityInDepartmentAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string dept, string code, CancellationToken _) => dept == "D-1" && code == "FA-1");
    }

    public void Dispose() => _context.Dispose();

    private CreateFormCommandHandler CreateHandler() => new(_context, _directory.Object, _user.Object, _clock);

    private UpdateFormCommandHandler UpdateHandler() => new(_context, _directory.Object, _user.Object, _clock);

    private static CreateFormCommand CreateCommand(string? department = "D-1", string? activity = "FA-1") => new()
    {
        Code = "FRM-001",
        NameEn = "Leak",
        NameAr = "تسرب",
        Category = FormCategories.Inspection,
        DepartmentCode = department,
        FieldActivityCode = activity,
    };

    [Fact]
    public async Task Create_StoresTheFieldActivity()
    {
        var result = await CreateHandler().Handle(CreateCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.DepartmentCode.Should().Be("D-1");
        result.Value.FieldActivityCode.Should().Be("FA-1");
    }

    [Fact]
    public async Task Create_RefusesAnActivityTheDepartmentDoesNotHave()
    {
        var result = await CreateHandler().Handle(CreateCommand(activity: "FA-OTHER"), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(FormEngineErrors.Codes.FormInvalidFieldActivity);
        _context.FormDefinitions.Should().BeEmpty();
    }

    [Fact]
    public async Task Update_DoesNotRecheckAnUnchangedActivity()
    {
        var form = FormDefinition.Create("FRM-002", "Old", "قديم", FormCategories.General, "D-9", "FA-RETIRED", "tester", Now);
        _context.FormDefinitions.Add(form);
        await _context.SaveChangesAsync();

        var result = await UpdateHandler().Handle(
            new UpdateFormCommand
            {
                Id = form.Id,
                NameEn = "Renamed",
                NameAr = "معدل",
                Category = FormCategories.General,
                DepartmentCode = "D-9",
                FieldActivityCode = "FA-RETIRED",
            },
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.NameEn.Should().Be("Renamed");
        _directory.Verify(
            d => d.IsFieldActivityInDepartmentAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Update_RefusesAChangedActivityOutsideTheDepartment()
    {
        var form = FormDefinition.Create("FRM-003", "Old", "قديم", FormCategories.General, "D-1", "FA-1", "tester", Now);
        _context.FormDefinitions.Add(form);
        await _context.SaveChangesAsync();

        var result = await UpdateHandler().Handle(
            new UpdateFormCommand
            {
                Id = form.Id,
                NameEn = "Old",
                NameAr = "قديم",
                Category = FormCategories.General,
                DepartmentCode = "D-1",
                FieldActivityCode = "FA-OTHER",
            },
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(FormEngineErrors.Codes.FormInvalidFieldActivity);
    }

    [Theory]
    [InlineData(null, "FA-1")]
    [InlineData("D-1", null)]
    [InlineData("", "")]
    public void Validators_RequireTheDepartmentAndTheActivity(string? department, string? activity)
    {
        new CreateFormCommandValidator().Validate(CreateCommand(department, activity)).IsValid.Should().BeFalse();

        new UpdateFormCommandValidator()
            .Validate(new UpdateFormCommand
            {
                Id = Guid.NewGuid(),
                NameEn = "a",
                NameAr = "b",
                Category = FormCategories.General,
                DepartmentCode = department,
                FieldActivityCode = activity,
            })
            .IsValid.Should().BeFalse();
    }
}
