using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Moq;
using NWFM.Shared.Constants;
using NWFM.Shared.Integration.Forms;
using Workflow.Api.Controllers;
using Workflow.Application.Workspace;

namespace NWFM.Tests.Workflow;

/// <summary>The designer's Form tab reads a form only through its activity's Department + FA Type, and checks that on the server.</summary>
public sealed class WorkspaceActivityFormsTests
{
    private static readonly Guid FormId = Guid.NewGuid();
    private readonly Mock<IFormGateway> _gateway = new(MockBehavior.Strict);
    private WorkspaceActivityForms Sut => new(_gateway.Object);

    private static FieldActivityFormInfo Form(string department = "10", string activity = "LEAK_REPAIR", bool usable = true) =>
        new(FormId, "DEMO-LEAK-REPAIR-COMPLETION", "Completion", "إنجاز", "INSPECTION", usable ? "PUBLISHED" : "DEPRECATED", department, activity, 2, [2, 1], usable, DateTime.UtcNow);

    [Theory]
    [InlineData(null, "LEAK_REPAIR")]
    [InlineData("10", null)]
    [InlineData(" ", "LEAK_REPAIR")]
    [InlineData("10", "")]
    public async Task List_WithoutBothCodes_RefusesInsteadOfListingTheCatalog(string? department, string? activity)
    {
        var result = await Sut.ListAsync(department, activity, 1, 20, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(WorkspaceActivityForms.ContextRequired);
        _gateway.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(0, 20)]
    [InlineData(1, 0)]
    [InlineData(1, WorkspaceActivityForms.MaxPageSize + 1)]
    public async Task List_RejectsBadPaging(int pageNumber, int pageSize)
    {
        (await Sut.ListAsync("10", "LEAK_REPAIR", pageNumber, pageSize, CancellationToken.None)).IsFailure.Should().BeTrue();
        _gateway.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task List_PassesBothTrimmedCodesAndThePage()
    {
        var page = new FieldActivityFormPage([Form()], 21, 21, 2, 20);
        _gateway.Setup(g => g.ListForFieldActivityAsync("10", "LEAK_REPAIR", 2, 20, It.IsAny<CancellationToken>())).ReturnsAsync(page);

        var result = await Sut.ListAsync(" 10 ", "LEAK_REPAIR ", 2, 20, CancellationToken.None);

        result.Value.Should().BeSameAs(page);
    }

    [Theory]
    [InlineData("10", "01")]
    [InlineData("11", "LEAK_REPAIR")]
    public async Task Preview_OfAFormFiledElsewhere_IsNotFound(string department, string activity)
    {
        _gateway.Setup(g => g.FindFieldActivityFormAsync(FormId, It.IsAny<CancellationToken>())).ReturnsAsync(Form());

        var result = await Sut.PreviewAsync(department, activity, FormId, 2, CancellationToken.None);

        result.Error.Should().Be(WorkspaceActivityForms.NotFound);
    }

    [Fact]
    public async Task Preview_OfAnUnpublishedVersion_IsNotFound()
    {
        _gateway.Setup(g => g.FindFieldActivityFormAsync(FormId, It.IsAny<CancellationToken>())).ReturnsAsync(Form());

        (await Sut.PreviewAsync("10", "LEAK_REPAIR", FormId, 3, CancellationToken.None)).Error.Should().Be(WorkspaceActivityForms.VersionNotFound);
    }

    [Fact]
    public async Task Preview_ReturnsTheFrozenSchemaOfThePinnedVersion_EvenForARetiredForm()
    {
        _gateway.Setup(g => g.FindFieldActivityFormAsync(FormId, It.IsAny<CancellationToken>())).ReturnsAsync(Form(usable: false));
        _gateway.Setup(g => g.GetVersionSchemaAsync(FormId, 1, It.IsAny<CancellationToken>())).ReturnsAsync("{\"elements\":[]}");

        var result = await Sut.PreviewAsync("10", "leak_repair", FormId, 1, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Match<ActivityFormPreview>(p => p.VersionNo == 1 && !p.IsUsable && p.SchemaJson == "{\"elements\":[]}");
    }

    [Fact]
    public void Endpoints_AreOpenToDesignersAndFormViewers_NotOnlyFormAdministrators()
    {
        var policy = typeof(WorkspaceActivityFormsController).GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>().Single().Policy;

        policy.Should().Be(NwfmPolicies.ActivityFormReaders);
        policy.Should().Contain(NwfmPolicies.ManageDefinitions).And.Contain(NwfmPolicies.ViewForms).And.NotContain(NwfmPolicies.ManageForms);
    }
}
