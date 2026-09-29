namespace NWFM.Tests.Modules.FormEngine;

using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using global::FormEngine.Application.Common.Interfaces;
using global::FormEngine.Application.Forms.Common;
using global::FormEngine.Domain.Constants;
using global::FormEngine.Domain.Entities;
using global::FormEngine.Infrastructure.Integration;
using global::FormEngine.Infrastructure.Persistence;
using global::FormEngine.Infrastructure.Persistence.Seed;
using NWFM.Shared.Caching;
using NWFM.Shared.Storage;

/// <summary>
/// The forms a workflow activity's Department + Field Activity Type are filed under, as the gateway lists them for the
/// designer's Form tab — and the seed that demonstrates them.
/// </summary>
public sealed class FieldActivityFormsTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 9, 29, 8, 0, 0, DateTimeKind.Utc);

    private readonly FormEngineDbContext _context = FormEngineTestData.CreateContext();
    private readonly FormPublisher _publisher;
    private readonly FormGateway _gateway;

    public FieldActivityFormsTests()
    {
        _publisher = new FormPublisher(_context, new Mock<IFormSubmissionStore>().Object, new Mock<ICacheService>().Object, new FakeTimeProvider(Now));
        _gateway = new FormGateway(_context, new Mock<IFormSubmissionStore>().Object, new Mock<IFormSubmissionService>().Object, new Mock<IFileStorage>().Object);
    }

    public void Dispose() => _context.Dispose();

    private async Task<FormDefinition> AddAsync(string code, string? department, string? activity, int publishes = 1)
    {
        var form = FormDefinition.Create(code, code + " EN", code + " AR", FormCategories.Inspection, department, activity, "tester", Now);
        form.SetSchema(FormEngineTestData.SimpleSchema(), null, null, "tester", Now);
        _context.FormDefinitions.Add(form);
        await _context.SaveChangesAsync();
        for (var i = 0; i < publishes; i++)
        {
            if (i > 0) form.SetSchema(FormEngineTestData.TwoFieldSchema, null, null, "tester", Now);
            (await _publisher.PublishAsync(form.Id, "tester", CancellationToken.None)).IsSuccess.Should().BeTrue();
        }

        return form;
    }

    /// <summary>Water Network / Leak repair has two usable forms, a draft, a deprecated and an archived one; the rest belong elsewhere.</summary>
    private async Task SeedCatalogAsync()
    {
        await AddAsync("LEAK-B", "10", "LEAK_REPAIR", publishes: 2);
        await AddAsync("LEAK-A", "10", "LEAK_REPAIR");
        await AddAsync("LEAK-DRAFT", "10", "LEAK_REPAIR", publishes: 0);
        var deprecated = await AddAsync("LEAK-OLD", "10", "LEAK_REPAIR");
        deprecated.Deprecate("tester", Now);
        var archived = await AddAsync("LEAK-GONE", "10", "LEAK_REPAIR");
        archived.Archive("tester", Now);
        await _context.SaveChangesAsync();

        await AddAsync("ISOLATION", "10", "01");
        await AddAsync("OTHER-DEPT", "11", "LEAK_REPAIR");
        await AddAsync("NO-CONTEXT", null, null);
    }

    [Fact]
    public async Task List_OnlyTheExactDepartmentAndFieldActivity_UsableFirst()
    {
        await SeedCatalogAsync();

        var page = await _gateway.ListForFieldActivityAsync("10", "LEAK_REPAIR", 1, 20, CancellationToken.None);

        page.TotalCount.Should().Be(5);
        page.UsableCount.Should().Be(2);
        page.Items.Select(f => f.Code).Should().Equal("LEAK-A", "LEAK-B", "LEAK-DRAFT", "LEAK-GONE", "LEAK-OLD");
        page.Items.Should().OnlyContain(f => f.DepartmentCode == "10" && f.FieldActivityCode == "LEAK_REPAIR");
        page.Items.Where(f => f.IsUsable).Select(f => f.Code).Should().BeEquivalentTo("LEAK-A", "LEAK-B");
    }

    [Fact]
    public async Task List_CarriesStatusAndEveryPublishedVersion()
    {
        await SeedCatalogAsync();

        var items = (await _gateway.ListForFieldActivityAsync("10", "LEAK_REPAIR", 1, 20, CancellationToken.None)).Items.ToDictionary(f => f.Code);

        items["LEAK-B"].VersionNos.Should().Equal(2, 1);
        items["LEAK-B"].CurrentVersionNo.Should().Be(2);
        items["LEAK-DRAFT"].Should().Match<global::NWFM.Shared.Integration.Forms.FieldActivityFormInfo>(f => f.CurrentVersionNo == null && f.VersionNos.Count == 0 && !f.IsUsable);
        items["LEAK-OLD"].Status.Should().Be(FormStatuses.Deprecated);
        items["LEAK-OLD"].IsUsable.Should().BeFalse();
        // History stays readable: a retired form keeps the versions a published workflow may still point at.
        items["LEAK-GONE"].VersionNos.Should().Equal(1);
    }

    [Fact]
    public async Task List_PagesThroughEveryMatch()
    {
        await SeedCatalogAsync();

        var codes = new List<string>();
        for (var pageNumber = 1; pageNumber <= 3; pageNumber++)
        {
            var page = await _gateway.ListForFieldActivityAsync("10", "LEAK_REPAIR", pageNumber, 2, CancellationToken.None);
            page.TotalCount.Should().Be(5);
            codes.AddRange(page.Items.Select(f => f.Code));
        }

        codes.Should().Equal("LEAK-A", "LEAK-B", "LEAK-DRAFT", "LEAK-GONE", "LEAK-OLD");
    }

    [Fact]
    public async Task List_NoMatch_IsEmptyRatherThanTheCatalog()
    {
        await SeedCatalogAsync();

        var page = await _gateway.ListForFieldActivityAsync("50", "02", 1, 20, CancellationToken.None);

        page.TotalCount.Should().Be(0);
        page.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Find_KnowsTheFormsContext_AndNothingForAFormWithoutOne()
    {
        var filed = await AddAsync("LEAK-A", "10", "LEAK_REPAIR");
        var loose = await AddAsync("NO-CONTEXT", null, null);

        (await _gateway.FindFieldActivityFormAsync(filed.Id, CancellationToken.None))!.FieldActivityCode.Should().Be("LEAK_REPAIR");
        (await _gateway.FindFieldActivityFormAsync(loose.Id, CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task Seed_PublishesTwoLeakRepairForms_LeavesTheDraft_AndIsIdempotent()
    {
        var databaseName = Guid.NewGuid().ToString();
        var services = new ServiceCollection();
        services.AddDbContext<FormEngineDbContext>(o => o.UseInMemoryDatabase(databaseName).ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)));
        services.AddScoped<IFormEngineDbContext>(sp => sp.GetRequiredService<FormEngineDbContext>());
        services.AddSingleton(new Mock<IFormSubmissionStore>().Object);
        services.AddSingleton(new Mock<ICacheService>().Object);
        services.AddSingleton<TimeProvider>(new FakeTimeProvider(Now));
        services.AddScoped<IFormPublisher, FormPublisher>();
        await using var provider = services.BuildServiceProvider();
        var scopes = provider.GetRequiredService<IServiceScopeFactory>();

        await FormSeedData.SeedAsync(scopes, NullLogger.Instance);

        // Someone edits a seeded, published form between runs; the next run must not touch it.
        using (var scope = scopes.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FormEngineDbContext>();
            var edited = await db.FormDefinitions.SingleAsync(f => f.Code == FormSeedData.LeakRepairCompletionCode);
            edited.UpdateDetails("Edited completion report", edited.NameAr, edited.Category, edited.DepartmentCode, edited.FieldActivityCode, "someone", Now);
            await db.SaveChangesAsync();
        }

        await FormSeedData.SeedAsync(scopes, NullLogger.Instance);

        using var check = scopes.CreateScope();
        var context = check.ServiceProvider.GetRequiredService<FormEngineDbContext>();
        var forms = await context.FormDefinitions.AsNoTracking().ToListAsync();
        forms.Select(f => f.Code).Should().OnlyHaveUniqueItems();
        var leak = forms.Where(f => f.DepartmentCode == "10" && f.FieldActivityCode == "LEAK_REPAIR").ToDictionary(f => f.Code);
        leak.Keys.Should().BeEquivalentTo(FormSeedData.LeakInspectionCode, FormSeedData.LeakRepairCompletionCode, FormSeedData.LeakRepairChecklistDraftCode);
        leak[FormSeedData.LeakInspectionCode].CurrentVersionNo.Should().Be(1);
        leak[FormSeedData.LeakRepairCompletionCode].CurrentVersionNo.Should().Be(1);
        leak[FormSeedData.LeakRepairCompletionCode].NameEn.Should().Be("Edited completion report");
        leak[FormSeedData.LeakRepairChecklistDraftCode].Status.Should().Be(FormStatuses.Draft);
        leak[FormSeedData.LeakRepairChecklistDraftCode].CurrentVersionNo.Should().BeNull();
        (await context.FormVersions.CountAsync(v => v.FormDefinitionId == leak[FormSeedData.LeakRepairCompletionCode].Id && v.TargetClient == FormTargetClients.Formly))
            .Should().Be(1, "a second run publishes nothing again");
    }
}
