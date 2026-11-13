namespace NWFM.Tests.Api;

using System.Security.Claims;
using Auth.Infrastructure.Identity;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using NWFM.Api.Services;
using NWFM.Shared.Integration.Forms;
using NWFM.Shared.Options;
using NWFM.Shared.Results;
using Tasks.Infrastructure.Persistence;

internal static class StartupActorFixture
{
    internal static (StartupActorContext Context, HttpContextAccessor Http, CurrentUser User) Create(Guid actor, bool administrator = true, bool locked = false)
    {
        var account = new ApplicationUser { Id = actor, UserName = "administrator@localhost" };
        var users = new Mock<UserManager<ApplicationUser>>(Mock.Of<IUserStore<ApplicationUser>>(), null!, null!, null!, null!, null!, null!, null!, null!);
        users.Setup(u => u.FindByNameAsync("administrator@localhost")).ReturnsAsync(account);
        users.Setup(u => u.IsInRoleAsync(account, "Administrator")).ReturnsAsync(administrator);
        users.Setup(u => u.IsLockedOutAsync(account)).ReturnsAsync(locked);
        var principals = new Mock<IUserClaimsPrincipalFactory<ApplicationUser>>();
        principals.Setup(p => p.CreateAsync(account)).ReturnsAsync(new ClaimsPrincipal(new ClaimsIdentity([
            new(ClaimTypes.NameIdentifier, actor.ToString()), new(ClaimTypes.Name, account.UserName), new(ClaimTypes.Role, "Administrator")], "Identity.Application")));
        var http = new HttpContextAccessor(); var context = new StartupActorContext(http, users.Object, principals.Object);
        return (context, http, new CurrentUser(http, context));
    }
}

public sealed class StartupInitializationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TaskSeedSubmissionFailureIsNotReportedAsSuccessfulInitialization(bool failedResult)
    {
        using var db = NWFM.Tests.Modules.Tasks.TaskTestData.CreateContext();
        var forms = new Mock<IFormGateway>(); var formId = Guid.NewGuid();
        var published = new PublishedFormInfo(formId, "DEMO-LEAK-INSPECTION", "Test", "Test", "Inspection", "Published", 1, true);
        forms.Setup(f => f.FindPublishedByCodeAsync(It.IsAny<string>(), default)).ReturnsAsync(published);
        forms.Setup(f => f.FindPublishedAsync(formId, default)).ReturnsAsync(published);
        if (failedResult) forms.Setup(f => f.SubmitAsync(It.IsAny<FormSubmitRequest>(), default))
            .ReturnsAsync(Result.Failure<FormSubmitReceipt>(new Error("seed.denied", "seed fill rejected")));
        else forms.Setup(f => f.SubmitAsync(It.IsAny<FormSubmitRequest>(), default)).ThrowsAsync(new InvalidOperationException("seed storage unavailable"));
        using var provider = new ServiceCollection().AddSingleton(db).AddSingleton(forms.Object).BuildServiceProvider();
        var initializer = new TasksDatabaseInitializer(Mock.Of<Microsoft.Extensions.Logging.ILogger<TasksDatabaseInitializer>>(), db,
            provider.GetRequiredService<IServiceScopeFactory>(), Options.Create(new DatabaseStartupOptions { SeedData = true }));
        Func<Task> seed = () => initializer.InitialiseAsync();
        await seed.Should().ThrowAsync<InvalidOperationException>().WithMessage(failedResult ? "*seed.denied*" : "seed storage unavailable");
        forms.Verify(f => f.SubmitAsync(It.IsAny<FormSubmitRequest>(), default), Times.Once);
    }

    [Fact]
    public async Task DisabledWorkflowMigrationAndSeedDoNotResolveOrTouchDatabase()
    {
        using var provider = new ServiceCollection().AddSingleton<IOptions<DatabaseStartupOptions>>(Options.Create(new DatabaseStartupOptions())).BuildServiceProvider();
        // No context is registered: either migration or seed access would fail this test.
        await DatabaseInitializer.MigrateAsync(provider);
        await DatabaseInitializer.SeedAsync(provider);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task StartupIdentityRejectsNonAdministratorOrLockedAccount(bool administrator, bool locked)
    {
        var f = StartupActorFixture.Create(Guid.NewGuid(), administrator, locked); var called = false;
        Func<Task> initialize = () => f.Context.RunWorkspaceDemoAsync(_ => { called = true; return Task.CompletedTask; });
        await initialize.Should().ThrowAsync<UnauthorizedAccessException>(); called.Should().BeFalse(); f.User.Id.Should().BeNull();
    }

    [Fact]
    public async Task UnauthenticatedHttpClaimsCannotUseStartupOrAdministratorIdentity()
    {
        var f = StartupActorFixture.Create(Guid.NewGuid());
        await f.Context.RunWorkspaceDemoAsync(_ =>
        {
            f.Http.HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([
                    new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()), new Claim(ClaimTypes.Role, "Administrator")]))
            };
            f.User.Id.Should().BeNull(); f.User.Roles.Should().BeEmpty(); f.User.Permissions.Should().BeEmpty();
            f.User.IsInRole("Administrator").Should().BeFalse();
            f.Http.HttpContext = null;
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task HttpRequestsCannotEnterOrInheritStartupIdentityAndFailureDisposesIt()
    {
        var actor = Guid.NewGuid(); var f = StartupActorFixture.Create(actor);
        f.Http.HttpContext = new DefaultHttpContext();
        Func<Task> forbidden = () => f.Context.RunWorkspaceDemoAsync(_ => Task.CompletedTask);
        await forbidden.Should().ThrowAsync<UnauthorizedAccessException>(); f.User.Id.Should().BeNull();
        f.Http.HttpContext = null;
        Func<Task> failure = () => f.Context.RunWorkspaceDemoAsync(async id =>
        {
            await Task.Yield(); id.Should().Be(actor); f.User.Id.Should().Be(actor.ToString()); f.User.IsInRole("Administrator").Should().BeTrue();
            f.Http.HttpContext = new DefaultHttpContext(); f.User.Id.Should().BeNull(); f.User.IsInRole("Administrator").Should().BeFalse();
            f.Http.HttpContext = null;
            throw new InvalidOperationException("seed failure");
        });
        await failure.Should().ThrowAsync<InvalidOperationException>().WithMessage("seed failure");
        f.User.Id.Should().BeNull(); f.Context.Principal.Should().BeNull();
    }
}
