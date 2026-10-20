using Auth.Application.Common.Interfaces;
using Auth.Application.Users.Commands.CreateUser;
using Auth.Application.Users.Commands.UpdateUser;
using Auth.Application.Users.Models;
using Auth.Domain.Constants;
using Auth.Domain.Entities;
using Auth.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Moq;
using NWFM.Shared.Results;

namespace NWFM.Tests.Security;

public sealed class UserScopeMutationSafetyTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task InvalidScopeCannotChangeRolesAccountOrExistingScopes(int invalidKind)
    {
        await using var db = CreateDb();
        var userId = Guid.NewGuid().ToString();
        var previous = await SeedScope(db, userId);
        var accounts = new Mock<IUserAccountService>(MockBehavior.Strict);
        var handler = new UpdateUserCommandHandler(accounts.Object, db);
        var invalid = invalidKind switch
        {
            0 => new OrgScopeAssignmentDto(),
            1 => new OrgScopeAssignmentDto { Level = "Branch" },
            2 => new OrgScopeAssignmentDto { Level = "invalid", Code = "1" },
            _ => null!
        };

        var result = await handler.Handle(new UpdateUserCommand
        {
            UserId = userId,
            Email = "changed@example.invalid",
            Roles = [Roles.Administrator],
            Scopes = [new() { DepartmentCodes = ["20"] }, invalid]
        }, default);

        Assert.True(result.IsFailure);
        Assert.Equal("Auth.InvalidScope", result.Error.Code);
        accounts.VerifyNoOtherCalls();
        Assert.False(db.ChangeTracker.HasChanges());
        await AssertOriginalScope(db, previous);
    }

    [Fact]
    public async Task InvalidScopeCannotCreateAnOrphanAccount()
    {
        await using var db = CreateDb();
        var accounts = new Mock<IUserAccountService>(MockBehavior.Strict);
        var handler = new CreateUserCommandHandler(accounts.Object, db);

        var result = await handler.Handle(new CreateUserCommand
        {
            UserName = "new-user",
            Password = "test-only-password",
            Roles = [Roles.Administrator],
            Scopes = [new()]
        }, default);

        Assert.True(result.IsFailure);
        Assert.Equal("Auth.InvalidScope", result.Error.Code);
        accounts.VerifyNoOtherCalls();
        Assert.Empty(await db.OrgScopes.ToListAsync());
        Assert.False(db.ChangeTracker.HasChanges());
    }

    [Fact]
    public async Task RejectedAccountUpdateCannotReplaceValidScopes()
    {
        await using var db = CreateDb();
        var userId = Guid.NewGuid().ToString();
        var previous = await SeedScope(db, userId);
        var accounts = new Mock<IUserAccountService>();
        accounts.Setup(x => x.UpdateUserAsync(userId, It.IsAny<EditedUserAccount>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<string>.Failure(new Error("Auth.UnknownRoles", "Unknown role.")));
        var handler = new UpdateUserCommandHandler(accounts.Object, db);

        var result = await handler.Handle(new UpdateUserCommand
        {
            UserId = userId,
            Roles = ["unknown"],
            Scopes = [new() { DepartmentCodes = ["20"] }]
        }, default);

        Assert.True(result.IsFailure);
        Assert.Equal("Auth.UnknownRoles", result.Error.Code);
        await AssertOriginalScope(db, previous);
    }

    [Fact]
    public async Task ValidUpdateStillChangesTheAccountAndReplacesItsScopes()
    {
        await using var db = CreateDb();
        var userId = Guid.NewGuid().ToString();
        var previous = await SeedScope(db, userId);
        var accounts = new Mock<IUserAccountService>();
        accounts.Setup(x => x.UpdateUserAsync(userId, It.IsAny<EditedUserAccount>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<string>.Success(userId));
        accounts.Setup(x => x.GetUserAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<UserAccount>.Success(new UserAccount { Id = userId, UserName = "user", Roles = [] }));
        var handler = new UpdateUserCommandHandler(accounts.Object, db);

        var result = await handler.Handle(new UpdateUserCommand
        {
            UserId = userId,
            Roles = [],
            Scopes = [new() { DepartmentCodes = ["20"] }]
        }, default);

        Assert.True(result.IsSuccess);
        accounts.Verify(x => x.UpdateUserAsync(userId,
            It.Is<EditedUserAccount>(account => account.Roles.Count == 0), It.IsAny<CancellationToken>()), Times.Once);
        var scope = await db.OrgScopes.Include(x => x.Departments).SingleAsync();
        Assert.NotEqual(previous.Id, scope.Id);
        Assert.Equal("20", Assert.Single(scope.Departments).DepartmentCode);
        Assert.Equal("20", Assert.Single(result.Value.Scopes).DepartmentCodes.Single());
    }

    private static AuthDbContext CreateDb() => new(
        new DbContextOptionsBuilder<AuthDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static async Task<OrgScope> SeedScope(AuthDbContext db, string userId)
    {
        var scope = OrgScope.Create(OrgScopeOwnerTypes.User, userId, null, null, ["10"]);
        db.OrgScopes.Add(scope);
        await db.SaveChangesAsync();
        return scope;
    }

    private static async Task AssertOriginalScope(AuthDbContext db, OrgScope previous)
    {
        var current = await db.OrgScopes.AsNoTracking().Include(x => x.Departments).SingleAsync();
        Assert.Equal(previous.Id, current.Id);
        Assert.Equal("10", Assert.Single(current.Departments).DepartmentCode);
    }
}
