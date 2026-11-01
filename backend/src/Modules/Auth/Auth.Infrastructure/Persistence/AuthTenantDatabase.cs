using System.Reflection;
using System.Text.RegularExpressions;
using Auth.Domain.Constants;
using Auth.Domain.Entities;
using Auth.Infrastructure.Identity;
using Auth.Infrastructure.Persistence.Seed;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NWFM.Shared.Constants;
using NWFM.Shared.MultiTenancy;

namespace Auth.Infrastructure.Persistence;

/// <summary>
/// The Auth module's part of every tenant database: EF migrations, SQL objects under
/// <c>sql/procedures</c>, and the seed (roles → permissions → grants → lookups, plus a default
/// administrator for the bootstrap workspace only). Run by the control plane inside the tenant's
/// scope; the tenant API never migrates.
/// </summary>
public sealed class AuthTenantDatabase(ILogger<AuthTenantDatabase> logger) : ITenantDatabaseModule
{
    /// <summary>Where the build drops the CREATE OR ALTER scripts, relative to the app base.</summary>
    private const string ProgrammableObjectsPath = "sql/procedures";

    private const string BootstrapAdminUserName = "administrator@localhost";
    private const string BootstrapAdminPassword = "Administrator1!";

    private static readonly Lazy<string?> Latest = new(() => LatestMigrationOf(typeof(AuthDbContext)));

    public string Name => "Auth";

    public int Order => 10;

    public string? LatestMigrationId => Latest.Value;

    public async Task MigrateAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var context = services.GetRequiredService<AuthDbContext>();
        await context.Database.MigrateAsync(cancellationToken);
        await ApplyProgrammableObjectsAsync(context, cancellationToken);
    }

    public async Task<IReadOnlyList<string>> GetPendingMigrationsAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var context = services.GetRequiredService<AuthDbContext>();
        return [.. await context.Database.GetPendingMigrationsAsync(cancellationToken)];
    }

    public async Task SeedAsync(IServiceProvider services, TenantSeedProfile profile, CancellationToken cancellationToken)
    {
        var context = services.GetRequiredService<AuthDbContext>();
        var roleManager = services.GetRequiredService<RoleManager<ApplicationRole>>();

        // Seed order: roles → permissions → grants → lookups → (bootstrap) admin user. Every step is idempotent.
        await SeedRolesAsync(roleManager);
        await SeedPermissionsAsync(context, cancellationToken);
        await SeedRolePermissionsAsync(context, roleManager, cancellationToken);
        await AuthLookupSeedData.SeedAsync(context);

        if (profile == TenantSeedProfile.Bootstrap)
            await SeedBootstrapAdministratorAsync(services.GetRequiredService<UserManager<ApplicationUser>>());
    }

    /// <summary>The same discovery EF does itself: migration classes carry [DbContext] and [Migration].</summary>
    public static string? LatestMigrationOf(Type contextType) =>
        contextType.Assembly.GetTypes()
            .Where(type => type.GetCustomAttribute<DbContextAttribute>()?.ContextType == contextType)
            .Select(type => type.GetCustomAttribute<MigrationAttribute>()?.Id)
            .Where(id => id is not null)
            .Max(StringComparer.Ordinal);

    /// <summary>
    /// Applies stored procedures and functions under sql/procedures. They stay out of EF migrations:
    /// a procedure is a replaceable definition, so CREATE OR ALTER keeps it in step with the file
    /// without accumulating one migration per edit.
    /// </summary>
    private async Task ApplyProgrammableObjectsAsync(AuthDbContext context, CancellationToken cancellationToken)
    {
        var directory = Path.Combine(AppContext.BaseDirectory, ProgrammableObjectsPath);
        if (!Directory.Exists(directory))
        {
            logger.LogWarning("No programmable SQL objects found at {Path}.", directory);
            return;
        }

        foreach (var file in Directory.EnumerateFiles(directory, "*.sql").Order(StringComparer.Ordinal))
        {
            var script = await File.ReadAllTextAsync(file, cancellationToken);

            // GO is a client-side separator ADO.NET does not understand, and CREATE OR ALTER has to
            // be the first statement of its batch, so the file is split before execution.
            var batches = Regex.Split(script, @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase);
            foreach (var batch in batches.Where(b => !string.IsNullOrWhiteSpace(b)))
                await context.Database.ExecuteSqlRawAsync(batch, cancellationToken);

            logger.LogInformation("Applied SQL object script {File}.", Path.GetFileName(file));
        }
    }

    private async Task SeedRolesAsync(RoleManager<ApplicationRole> roleManager)
    {
        foreach (var roleName in Roles.All)
        {
            if (!await roleManager.RoleExistsAsync(roleName))
            {
                await roleManager.CreateAsync(new ApplicationRole(roleName));
                logger.LogInformation("Created role {Role}.", roleName);
            }
        }
    }

    private async Task SeedPermissionsAsync(AuthDbContext context, CancellationToken cancellationToken)
    {
        var existingCodes = await context.Permissions
            .Select(p => p.Code)
            .ToHashSetAsync(StringComparer.Ordinal, cancellationToken);

        var permissionsToSeed = PermissionCatalog
            .Where(p => !existingCodes.Contains(p.Code))
            .Select(p => Permission.Create(p.Code, p.Module, p.NameEn, p.NameAr))
            .ToList();

        if (permissionsToSeed.Count > 0)
        {
            context.Permissions.AddRange(permissionsToSeed);
            await context.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Seeded {Count} permissions.", permissionsToSeed.Count);
        }
    }

    private async Task SeedRolePermissionsAsync(
        AuthDbContext context, RoleManager<ApplicationRole> roleManager, CancellationToken cancellationToken)
    {
        var permissions = await context.Permissions.ToListAsync(cancellationToken);
        var permissionByCode = permissions.ToDictionary(p => p.Code, StringComparer.Ordinal);

        var existing = (await context.RolePermissions
                .Select(rp => new { rp.RoleId, rp.PermissionId })
                .ToListAsync(cancellationToken))
            .Select(x => $"{x.RoleId}|{x.PermissionId}")
            .ToHashSet(StringComparer.Ordinal);

        var toAdd = new List<RolePermission>();
        foreach (var (roleName, permissionCodes) in RolePermissionMatrix)
        {
            var role = await roleManager.FindByNameAsync(roleName);
            if (role is null) continue;

            // Administrator holds every permission; the matrix names the rest explicitly.
            IEnumerable<string> codes = roleName == Roles.Administrator ? permissionByCode.Keys : permissionCodes;
            foreach (var code in codes)
            {
                if (!permissionByCode.TryGetValue(code, out var permission)) continue;
                if (!existing.Add($"{role.Id}|{permission.Id}")) continue;
                toAdd.Add(RolePermission.Create(role.Id.ToString(), permission.Id));
            }
        }

        if (toAdd.Count > 0)
        {
            context.RolePermissions.AddRange(toAdd);
            await context.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Seeded {Count} role-permission mappings.", toAdd.Count);
        }
    }

    private async Task SeedBootstrapAdministratorAsync(UserManager<ApplicationUser> userManager)
    {
        if (await userManager.FindByNameAsync(BootstrapAdminUserName) is not null)
            return;

        var admin = new ApplicationUser
        {
            UserName = BootstrapAdminUserName,
            Email = BootstrapAdminUserName,
            EmailConfirmed = true
        };

        var result = await userManager.CreateAsync(admin, BootstrapAdminPassword);
        if (result.Succeeded)
        {
            await userManager.AddToRoleAsync(admin, Roles.Administrator);
            logger.LogInformation("Seeded bootstrap administrator {UserName}.", BootstrapAdminUserName);
        }
        else
        {
            logger.LogWarning("Failed to seed the bootstrap administrator: {Errors}",
                string.Join(", ", result.Errors.Select(e => e.Description)));
        }
    }

    private static readonly Dictionary<string, string[]> RolePermissionMatrix = new()
    {
        [Roles.Administrator] = [],

        [Roles.WorkflowAdmin] =
        [
            NwfmPolicies.ViewWorkflows, NwfmPolicies.StartWorkflows, NwfmPolicies.ClaimTasks,
            NwfmPolicies.ManageDefinitions, NwfmPolicies.ManageBindings, NwfmPolicies.ManageCalendars,
            NwfmPolicies.ManageSlaPolicies, NwfmPolicies.ViewInstances, NwfmPolicies.ManageIncidents,
            NwfmPolicies.ManageDeadLetters, NwfmPolicies.ManageParticipants, NwfmPolicies.ManageGroups,
            NwfmPolicies.ViewWorkload, NwfmPolicies.ManageLookups,
            NwfmPolicies.ViewForms, NwfmPolicies.ManageForms, NwfmPolicies.SubmitForms,
            NwfmPolicies.ViewSubmissions,
            NwfmPolicies.ManageTeams,
            NwfmPolicies.ViewTasks, NwfmPolicies.ManageTasks, NwfmPolicies.AssignTasks,
            NwfmPolicies.SubmitTasks, NwfmPolicies.ReviewTasks, NwfmPolicies.ManageTaskTypes
        ],

        [Roles.Supervisor] =
        [
            NwfmPolicies.ViewWorkflows, NwfmPolicies.StartWorkflows, NwfmPolicies.ClaimTasks,
            NwfmPolicies.ViewInstances, NwfmPolicies.ManageIncidents,
            NwfmPolicies.ViewWorkload,
            NwfmPolicies.ViewForms, NwfmPolicies.SubmitForms, NwfmPolicies.ViewSubmissions,
            NwfmPolicies.ViewTasks, NwfmPolicies.AssignTasks, NwfmPolicies.ReviewTasks,
            NwfmPolicies.SubmitTasks
        ],

        [Roles.Participant] =
        [
            NwfmPolicies.ViewWorkflows, NwfmPolicies.ClaimTasks,
            NwfmPolicies.ViewInstances,
            NwfmPolicies.ViewForms, NwfmPolicies.SubmitForms,
            NwfmPolicies.ViewTasks, NwfmPolicies.SubmitTasks
        ],

        [Roles.FieldTeam] =
        [
            NwfmPolicies.ViewWorkflows, NwfmPolicies.ClaimTasks,
            NwfmPolicies.SubmitForms,
            // A crew sees only the tasks assigned to its own team; the Tasks module enforces that.
            NwfmPolicies.ViewTasks, NwfmPolicies.SubmitTasks
        ],

        [Roles.Monitor] =
        [
            NwfmPolicies.ViewWorkflows, NwfmPolicies.ViewInstances,
            NwfmPolicies.ViewWorkload,
            NwfmPolicies.ViewForms, NwfmPolicies.ViewSubmissions,
            NwfmPolicies.ViewTasks
        ]
    };

    private static readonly (string Code, string Module, string NameEn, string NameAr)[] PermissionCatalog =
    [
        (NwfmPolicies.CanManageRolePermissions, "Admin", "Manage role permissions", "إدارة صلاحيات الأدوار"),
        (NwfmPolicies.CanPurge, "Admin", "Purge data", "حذف البيانات"),
        (NwfmPolicies.ManageUsers, "Admin", "Manage user accounts", "إدارة حسابات المستخدمين"),
        (NwfmPolicies.ManageLookups, "Admin", "Manage reference data", "إدارة البيانات المرجعية"),
        (NwfmPolicies.ManageSupportAccess, "Admin", "Manage platform support access", "إدارة وصول فريق الدعم"),
        (NwfmPolicies.ViewWorkflows, "Workflow", "View workflows", "عرض سير العمل"),
        (NwfmPolicies.StartWorkflows, "Workflow", "Start workflows", "بدء سير العمل"),
        (NwfmPolicies.ClaimTasks, "Workflow", "Claim tasks", "استلام المهام"),
        (NwfmPolicies.ManageDefinitions, "Workflow", "Manage definitions", "إدارة التعريفات"),
        (NwfmPolicies.ManageBindings, "Workflow", "Manage bindings", "إدارة الارتباطات"),
        (NwfmPolicies.ManageCalendars, "Workflow", "Manage calendars", "إدارة التقويم"),
        (NwfmPolicies.ManageSlaPolicies, "Workflow", "Manage SLA policies", "إدارة سياسات الخدمة"),
        (NwfmPolicies.ViewInstances, "Workflow", "View instances", "عرض التنفيذ"),
        (NwfmPolicies.ManageIncidents, "Workflow", "Manage incidents", "إدارة الحوادث"),
        (NwfmPolicies.ManageDeadLetters, "Workflow", "Manage dead letters", "إدارة الرسائل المتعثرة"),
        (NwfmPolicies.ManageParticipants, "Workflow", "Manage participants", "إدارة المشاركين"),
        (NwfmPolicies.ManageGroups, "Workflow", "Manage assignment groups", "إدارة مجموعات الإسناد"),
        (NwfmPolicies.ViewWorkload, "Workflow", "View workload", "عرض عبء العمل"),
        (NwfmPolicies.ViewForms, "Forms", "View forms", "عرض النماذج"),
        (NwfmPolicies.ManageForms, "Forms", "Manage forms", "إدارة النماذج"),
        (NwfmPolicies.SubmitForms, "Forms", "Fill and submit forms", "تعبئة وإرسال النماذج"),
        (NwfmPolicies.ViewSubmissions, "Forms", "View form submissions", "عرض إرساليات النماذج"),
        (NwfmPolicies.ManageTeams, "Admin", "Manage field teams", "إدارة الفرق الميدانية"),
        (NwfmPolicies.ViewTasks, "Tasks", "View tasks", "عرض المهام"),
        (NwfmPolicies.ManageTasks, "Tasks", "Create and manage tasks", "إنشاء المهام وإدارتها"),
        (NwfmPolicies.AssignTasks, "Tasks", "Assign tasks to teams", "إسناد المهام إلى الفرق"),
        (NwfmPolicies.SubmitTasks, "Tasks", "Fill tasks", "تعبئة المهام"),
        (NwfmPolicies.ReviewTasks, "Tasks", "Approve and return tasks", "اعتماد المهام وإرجاعها"),
        (NwfmPolicies.ManageTaskTypes, "Tasks", "Manage task types", "إدارة أنواع المهام")
    ];
}
