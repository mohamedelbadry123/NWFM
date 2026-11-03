using Auth.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace NWFM.Tests.Security;

public sealed class AuthTenantInventoryTests
{
    [Fact]
    public void EveryAuthEntityIsClassifiedAsDedicatedTenantDatabaseData()
    {
        using var db = new AuthDbContext(new DbContextOptionsBuilder<AuthDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        string[] deploymentOwnedTables =
        [
            "LKP_ACTIVITY_SOURCE", "LKP_BRANCH", "LKP_CBU", "LKP_CLUSTER", "LKP_DEPARTMENT",
            "LKP_FIELD_ACTIVITY_TYPE", "LKP_FIELD_ACTIVITY_TYPE_SOURCE", "LKP_OPERATION_AREA",
            "OrgScopes", "OrgScopesActivityType", "OrgScopesDepartment", "Permissions",
            "RolePermissions", "SsoAuthorizationCodes", "Teams", "RefreshTokens", "Roles",
            "Users", "RoleClaims", "UserClaims", "UserLogins", "UserRoles", "UserTokens"
        ];
        var entities = db.Model.GetEntityTypes().ToArray();
        Assert.Equal(deploymentOwnedTables.Order(), entities.Select(x => x.GetTableName()).Order());
        Assert.All(entities, entity => Assert.Equal("Auth", entity.GetSchema()));
    }
}
