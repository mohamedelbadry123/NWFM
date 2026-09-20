using System.Text.RegularExpressions;
using Auth.Domain.Constants;
using Auth.Domain.Entities;
using Auth.Infrastructure.Identity;
using Auth.Infrastructure.Persistence.Seed;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NWFM.Shared.Constants;
using NWFM.Shared.Options;

namespace Auth.Infrastructure.Persistence;

/// <summary>
/// Three-flag database initializer for the Auth module. Mirrors the reference app pattern:
/// 1. ApplyMigrations  → EF Core MigrateAsync
/// 2. ApplySqlObjects  → execute sql/procedures/*.sql (CREATE OR ALTER, split on GO)
/// 3. SeedData         → idempotent roles, permissions, grants, lookups, admin user
/// </summary>
public sealed class AuthDatabaseInitializer
{
    /// <summary>Where the build drops the CREATE OR ALTER scripts, relative to the app base.</summary>
    private const string ProgrammableObjectsPath = "sql/procedures";

    private readonly ILogger<AuthDatabaseInitializer> _logger;
    private readonly AuthDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<ApplicationRole> _roleManager;
    private readonly DatabaseStartupOptions _startupOptions;

    public AuthDatabaseInitializer(
        ILogger<AuthDatabaseInitializer> logger,
        AuthDbContext context,
        UserManager<ApplicationUser> userManager,
        RoleManager<ApplicationRole> roleManager,
        IOptions<DatabaseStartupOptions> startupOptions)
    {
        _logger = logger;
        _context = context;
        _userManager = userManager;
        _roleManager = roleManager;
        _startupOptions = startupOptions.Value;
    }

    public async Task InitialiseAsync()
    {
        _logger.LogInformation(
            "Auth DatabaseStartup settings: ApplyMigrations={ApplyMigrations}, ApplySqlObjects={ApplySqlObjects}, SeedData={SeedData}.",
            _startupOptions.ApplyMigrations,
            _startupOptions.ApplySqlObjects,
            _startupOptions.SeedData);

        try
        {
            if (_startupOptions.ApplyMigrations)
            {
                _logger.LogInformation("Applying Auth EF Core migrations (DatabaseStartup:ApplyMigrations=true).");
                await _context.Database.MigrateAsync();
                _logger.LogInformation("Auth EF Core migrations completed.");
            }
            else
            {
                _logger.LogInformation("Skipping Auth EF migrations (DatabaseStartup:ApplyMigrations=false).");
            }

            if (_startupOptions.ApplySqlObjects)
            {
                _logger.LogInformation(
                    "Applying SQL programmable objects from {Path} (DatabaseStartup:ApplySqlObjects=true).",
                    ProgrammableObjectsPath);
                await ApplyProgrammableObjectsAsync();
                _logger.LogInformation("SQL programmable objects step completed.");
            }
            else
            {
                _logger.LogInformation("Skipping SQL programmable objects (DatabaseStartup:ApplySqlObjects=false).");
            }

            if (_startupOptions.SeedData)
            {
                _logger.LogInformation("Seeding Auth database (DatabaseStartup:SeedData=true).");
                await SeedAsync();
                _logger.LogInformation("Auth database seed completed.");
            }
            else
            {
                _logger.LogInformation("Skipping Auth database seed (DatabaseStartup:SeedData=false).");
            }

            _logger.LogInformation("Auth database startup initialisation finished.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred while initialising the Auth database.");
            throw;
        }
    }

    /// <summary>
    /// Applies stored procedures and functions under sql/procedures.
    /// They stay out of EF migrations: a procedure is a replaceable definition,
    /// so CREATE OR ALTER on startup keeps it in step with the file without
    /// accumulating one migration per edit.
    /// </summary>
    private async Task ApplyProgrammableObjectsAsync()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, ProgrammableObjectsPath);

        if (!Directory.Exists(directory))
        {
            _logger.LogWarning("No programmable SQL objects found at {Path}.", directory);
            return;
        }

        var files = Directory.EnumerateFiles(directory, "*.sql").Order(StringComparer.Ordinal).ToList();
        _logger.LogInformation(
            "Found {Count} SQL object script(s) under {Path}.",
            files.Count,
            directory);

        foreach (var file in files)
        {
            var script = await File.ReadAllTextAsync(file);

            // GO is a client-side separator that ADO.NET does not understand, and
            // CREATE OR ALTER has to be the first statement of its batch — so the
            // file is split before execution.
            var batches = Regex.Split(
                script,
                @"^\s*GO\s*$",
                RegexOptions.Multiline | RegexOptions.IgnoreCase);

            foreach (var batch in batches)
            {
                if (string.IsNullOrWhiteSpace(batch))
                    continue;

                await _context.Database.ExecuteSqlRawAsync(batch);
            }

            _logger.LogInformation("Applied SQL object script {File}.", Path.GetFileName(file));
        }
    }

    private async Task SeedAsync()
    {
        try
        {
            await TrySeedAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred while seeding the Auth database.");
            throw;
        }
    }

    private async Task TrySeedAsync()
    {
        // Seed order: roles → permissions → grants → lookups → admin user.
        await SeedRolesAsync();
        await SeedPermissionsAsync();
        await SeedRolePermissionsAsync();
        await AuthLookupSeedData.SeedAsync(_context);
        await SeedAdministratorAsync();
    }

    private async Task SeedRolesAsync()
    {
        foreach (var roleName in Roles.All)
        {
            if (!await _roleManager.RoleExistsAsync(roleName))
            {
                await _roleManager.CreateAsync(new ApplicationRole(roleName));
                _logger.LogInformation("Created role: {Role}", roleName);
            }
        }
    }

    private async Task SeedPermissionsAsync()
    {
        var existingCodes = await _context.Permissions
            .Select(p => p.Code)
            .ToHashSetAsync(StringComparer.Ordinal);

        var permissionsToSeed = PermissionCatalog
            .Where(p => !existingCodes.Contains(p.Code))
            .Select(p => Permission.Create(p.Code, p.Module, p.NameEn, p.NameAr))
            .ToList();

        if (permissionsToSeed.Count > 0)
        {
            _context.Permissions.AddRange(permissionsToSeed);
            await _context.SaveChangesAsync();
            _logger.LogInformation("Seeded {Count} permissions.", permissionsToSeed.Count);
        }
    }

    private async Task SeedRolePermissionsAsync()
    {
        var permissions = await _context.Permissions.ToListAsync();
        var permissionByCode = permissions.ToDictionary(p => p.Code, StringComparer.Ordinal);

        var existingMappings = await _context.RolePermissions
            .Select(rp => new { rp.RoleId, rp.PermissionId })
            .ToListAsync();
        var existingSet = existingMappings
            .Select(x => $"{x.RoleId}|{x.PermissionId}")
            .ToHashSet(StringComparer.Ordinal);

        var matrix = GetRolePermissionMatrix();
        var toAdd = new List<RolePermission>();

        foreach (var (roleName, permissionCodes) in matrix)
        {
            var role = await _roleManager.FindByNameAsync(roleName);
            if (role is null) continue;

            foreach (var code in permissionCodes)
            {
                if (!permissionByCode.TryGetValue(code, out var permission)) continue;
                var key = $"{role.Id}|{permission.Id}";
                if (existingSet.Contains(key)) continue;
                toAdd.Add(RolePermission.Create(role.Id.ToString(), permission.Id));
            }
        }

        if (toAdd.Count > 0)
        {
            _context.RolePermissions.AddRange(toAdd);
            await _context.SaveChangesAsync();
            _logger.LogInformation("Seeded {Count} role-permission mappings.", toAdd.Count);
        }

        // Administrator blanket grant: every permission.
        var adminRole = await _roleManager.FindByNameAsync(Roles.Administrator);
        if (adminRole is not null)
        {
            var adminExisting = await _context.RolePermissions
                .Where(rp => rp.RoleId == adminRole.Id.ToString())
                .Select(rp => rp.PermissionId)
                .ToHashSetAsync();

            var adminToAdd = permissions
                .Where(p => !adminExisting.Contains(p.Id))
                .Select(p => RolePermission.Create(adminRole.Id.ToString(), p.Id))
                .ToList();

            if (adminToAdd.Count > 0)
            {
                _context.RolePermissions.AddRange(adminToAdd);
                await _context.SaveChangesAsync();
                _logger.LogInformation("Granted {Count} remaining permissions to Administrator.", adminToAdd.Count);
            }
        }
    }

    private async Task SeedAdministratorAsync()
    {
        const string adminUserName = "administrator@localhost";

        if (await _userManager.FindByNameAsync(adminUserName) is not null)
            return;

        var admin = new ApplicationUser
        {
            UserName = adminUserName,
            Email = adminUserName,
            EmailConfirmed = true
        };

        var result = await _userManager.CreateAsync(admin, "Administrator1!");
        if (result.Succeeded)
        {
            await _userManager.AddToRoleAsync(admin, Roles.Administrator);
            _logger.LogInformation("Seeded administrator account: {UserName}", adminUserName);
        }
        else
        {
            _logger.LogWarning("Failed to seed administrator: {Errors}",
                string.Join(", ", result.Errors.Select(e => e.Description)));
        }
    }

    private static Dictionary<string, string[]> GetRolePermissionMatrix()
    {
        return new Dictionary<string, string[]>
        {
            [Roles.WorkflowAdmin] =
            [
                NwfmPolicies.ViewWorkflows, NwfmPolicies.StartWorkflows, NwfmPolicies.ClaimTasks,
                NwfmPolicies.ManageDefinitions, NwfmPolicies.ManageBindings, NwfmPolicies.ManageCalendars,
                NwfmPolicies.ManageSlaPolicies, NwfmPolicies.ViewInstances, NwfmPolicies.ManageIncidents,
                NwfmPolicies.ManageDeadLetters, NwfmPolicies.ManageParticipants, NwfmPolicies.ManageGroups,
                NwfmPolicies.ViewWorkload, NwfmPolicies.ManageLookups,
                NwfmPolicies.ViewForms, NwfmPolicies.ManageForms, NwfmPolicies.SubmitForms,
                NwfmPolicies.ViewSubmissions
            ],

            [Roles.Supervisor] =
            [
                NwfmPolicies.ViewWorkflows, NwfmPolicies.StartWorkflows, NwfmPolicies.ClaimTasks,
                NwfmPolicies.ViewInstances, NwfmPolicies.ManageIncidents,
                NwfmPolicies.ViewWorkload,
                NwfmPolicies.ViewForms, NwfmPolicies.SubmitForms, NwfmPolicies.ViewSubmissions
            ],

            [Roles.Participant] =
            [
                NwfmPolicies.ViewWorkflows, NwfmPolicies.ClaimTasks,
                NwfmPolicies.ViewInstances,
                NwfmPolicies.ViewForms, NwfmPolicies.SubmitForms
            ],

            [Roles.FieldTeam] =
            [
                NwfmPolicies.ViewWorkflows, NwfmPolicies.ClaimTasks,
                NwfmPolicies.SubmitForms
            ],

            [Roles.Monitor] =
            [
                NwfmPolicies.ViewWorkflows, NwfmPolicies.ViewInstances,
                NwfmPolicies.ViewWorkload,
                NwfmPolicies.ViewForms, NwfmPolicies.ViewSubmissions
            ]
        };
    }

    private static readonly (string Code, string Module, string NameEn, string NameAr)[] PermissionCatalog =
    [
        (NwfmPolicies.CanManageRolePermissions, "Admin", "Manage role permissions", "إدارة صلاحيات الأدوار"),
        (NwfmPolicies.CanPurge, "Admin", "Purge data", "حذف البيانات"),
        (NwfmPolicies.ManageUsers, "Admin", "Manage user accounts", "إدارة حسابات المستخدمين"),
        (NwfmPolicies.ManageLookups, "Admin", "Manage reference data", "إدارة البيانات المرجعية"),
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
        (NwfmPolicies.ViewSubmissions, "Forms", "View form submissions", "عرض إرساليات النماذج")
    ];
}
