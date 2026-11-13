using System.Text;
using Auth.Application.Common.Interfaces;
using Auth.Domain.Constants;
using Auth.Domain.Options;
using Auth.Infrastructure.Authorization;
using Auth.Infrastructure.Caching;
using Auth.Infrastructure.Identity;
using Auth.Infrastructure.Identity.ActiveDirectory;
using Auth.Infrastructure.Identity.Sso;
using Auth.Infrastructure.Operations;
using Auth.Infrastructure.Organization;
using Auth.Infrastructure.Persistence;
using Auth.Infrastructure.Services;
using Auth.Infrastructure.Services.Messaging;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using NWFM.Shared.Abstractions;
using NWFM.Shared.Integration.Organization;
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
        var tenantId = builder.Configuration.GetValue<Guid>("Application:TenantId");
        builder.Services.AddDataProtection();
        builder.Services.AddOptions<CookieAuthenticationOptions>(SsoDefaults.TempCookieScheme)
            .Configure<IDataProtectionProvider>((options, provider) =>
                options.DataProtectionProvider = provider.CreateProtector(
                    "NWFM.Auth.TenantCookies.v1", builder.Environment.EnvironmentName, tenantId.ToString("D")));

        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddScoped<IPermissionResolver, PermissionResolver>();
        builder.Services.AddScoped<IAuthService, AuthService>();
        builder.Services.AddScoped<TenantSessionValidator>();
        builder.Services.AddScoped<ITenantAvailability>(sp =>
            new SqlTenantAvailability(connectionString, sp.GetRequiredService<ICurrentTenant>()));
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
        var jwtValidator = new JwtSettingsValidator(builder.Environment);
        var jwtValidation = jwtValidator.Validate(Options.DefaultName, jwtSettings);
        if (jwtValidation.Failed)
            throw new OptionsValidationException(Options.DefaultName, typeof(JwtSettings), jwtValidation.Failures);
        builder.Services.AddSingleton<IValidateOptions<JwtSettings>>(jwtValidator);
        builder.Services.AddOptions<JwtSettings>()
            .BindConfiguration(JwtSettings.SectionName)
            .ValidateOnStart();
        builder.Services.AddOptions<TenantIsolationOptions>()
            .BindConfiguration(TenantIsolationOptions.SectionName)
            .Validate(options => options.TenantId != Guid.Empty, "Tenant ID is required.")
            .ValidateOnStart();
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
                        ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                        ClockSkew = TimeSpan.FromSeconds(30),
                        ValidIssuer = jwtSettings.Issuer,
                        ValidAudience = jwtSettings.Audience,
                        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.SigningKey))
                    };
                    options.Events = new JwtBearerEvents
                    {
                        OnTokenValidated = async context =>
                        {
                            var validator = context.HttpContext.RequestServices
                                .GetRequiredService<TenantSessionValidator>();
                            var principal = await validator.ValidateAsync(context.Principal!,
                                context.HttpContext.RequestAborted);
                            if (principal is null)
                            {
                                var logger = context.HttpContext.RequestServices
                                    .GetRequiredService<ILogger<TenantSessionValidator>>();
                                logger.LogWarning(new EventId(4101, "TenantSessionDenied"),
                                    "Tenant session rejected for deployment {TenantId}.",
                                    context.HttpContext.RequestServices
                                        .GetRequiredService<NWFM.Shared.Abstractions.ICurrentTenant>().OrganizationId);
                                context.Fail("The tenant session is no longer authorized.");
                                return;
                            }
                            context.Principal = principal;
                        }
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
