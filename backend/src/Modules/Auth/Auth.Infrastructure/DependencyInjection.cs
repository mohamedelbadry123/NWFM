using Auth.Infrastructure.Organization;
using NWFM.Shared.Integration.Organization;
using System.Text;
using Auth.Application.Common.Interfaces;
using Auth.Domain.Options;
using Auth.Infrastructure.Authorization;
using Auth.Infrastructure.Identity;
using Auth.Infrastructure.Identity.ActiveDirectory;
using Auth.Infrastructure.Identity.Sso;
using Auth.Infrastructure.Persistence;
using Auth.Infrastructure.Services;
using Auth.Infrastructure.Services.Messaging;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Auth.Infrastructure.Caching;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using NWFM.Shared.Options;

namespace Auth.Infrastructure;

public static class DependencyInjection
{
    public static void AddAuthInfrastructure(
        this IHostApplicationBuilder builder,
        string connectionString)
    {
        builder.Services.AddDbContext<AuthDbContext>(options =>
            options.UseSqlServer(connectionString));
        builder.Services.AddScoped<IAuthDbContext>(sp => sp.GetRequiredService<AuthDbContext>());
        builder.Services.AddScoped<NWFM.Shared.Integration.Workflow.IWorkflowReferenceData, WorkflowReferenceData>();

        builder.Services
            .AddIdentity<ApplicationUser, ApplicationRole>()
            .AddEntityFrameworkStores<AuthDbContext>()
            .AddClaimsPrincipalFactory<ApplicationUserClaimsPrincipalFactory>()
            .AddDefaultTokenProviders();

        builder.Services.AddCachingServices(builder.Configuration);

        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddScoped<IPermissionResolver, PermissionResolver>();
        builder.Services.AddScoped<IAuthService, AuthService>();
        builder.Services.AddScoped<IUserAccountService, UserAccountService>();

        // Territory for the modules that narrow work to it (Tasks), without referencing Auth.
        builder.Services.AddScoped<OrgScopeProvider>();
        builder.Services.AddScoped<IOrgScopeProvider>(sp => sp.GetRequiredService<OrgScopeProvider>());
        builder.Services.AddScoped<IOrgDirectory>(sp => sp.GetRequiredService<OrgScopeProvider>());
        builder.Services.AddScoped<IRoleLookup, RoleLookup>();

        builder.Services.AddAuthorization();
        builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
        builder.Services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();

        JwtSettings jwtSettings = builder.Configuration.GetSection(JwtSettings.SectionName).Get<JwtSettings>() ?? new JwtSettings();
        builder.Services.Configure<JwtSettings>(builder.Configuration.GetSection(JwtSettings.SectionName));
        builder.Services.Configure<SsoSettings>(builder.Configuration.GetSection(SsoSettings.SectionName));
        builder.Services.Configure<ActiveDirectorySettings>(builder.Configuration.GetSection(ActiveDirectorySettings.SectionName));
        builder.Services.Configure<TeamOtpOptions>(builder.Configuration.GetSection(TeamOtpOptions.SectionName));
        builder.Services.Configure<ExternalConsumersOptions>(builder.Configuration.GetSection(ExternalConsumersOptions.SectionName));
        builder.Services.Configure<ApiSecurityOptions>(builder.Configuration.GetSection(ApiSecurityOptions.SectionName));
        builder.Services.Configure<TeamGatewayOptions>(builder.Configuration.GetSection(TeamGatewayOptions.SectionName));

        builder.Services.AddSingleton<IActiveDirectoryAuthenticator, LdapActiveDirectoryAuthenticator>();
        builder.Services.AddScoped<ISmsSender, LoggingSmsSender>();

        builder.Services.Configure<DatabaseStartupOptions>(
            builder.Configuration.GetSection(DatabaseStartupOptions.SectionName));
        builder.Services.AddScoped<AuthDatabaseInitializer>();

        if (!string.IsNullOrWhiteSpace(jwtSettings.SigningKey))
        {
            builder.Services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
                })
                .AddJwtBearer(options =>
                {
                    options.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuer = true,
                        ValidateAudience = true,
                        ValidateLifetime = true,
                        ValidateIssuerSigningKey = true,
                        ValidIssuer = jwtSettings.Issuer,
                        ValidAudience = jwtSettings.Audience,
                        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.SigningKey))
                    };
                })
                .AddSaml2Sso(builder.Configuration, builder.Environment);
        }
    }

    /// <summary>
    /// Runs the Auth database initialiser (migrate, SQL objects, seed) using the
    /// three-flag <see cref="DatabaseStartupOptions"/> pattern from the reference app.
    /// Call after <c>builder.Build()</c>, before <c>app.Run()</c>.
    /// </summary>
    public static async Task InitialiseAuthDatabaseAsync(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var initialiser = scope.ServiceProvider.GetRequiredService<AuthDatabaseInitializer>();
        await initialiser.InitialiseAsync();
    }
}
