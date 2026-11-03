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
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using NWFM.Shared.Abstractions;
using NWFM.Shared.Integration.Organization;
using NWFM.Shared.MultiTenancy;

namespace Auth.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// The Auth tables in a tenant database: the DbContext (connected to the ambient tenant), Identity
    /// stores and token providers. Shared by the tenant API and the control plane, which migrates,
    /// seeds and creates tenant administrators through the same stores.
    /// </summary>
    public static void AddAuthPersistence(this IHostApplicationBuilder builder)
    {
        builder.Services.AddDbContext<AuthDbContext>((sp, options) =>
            options.UseSqlServer(sp.GetRequiredService<ITenantConnectionStringResolver>().ResolveForCurrentTenant()));
        builder.Services.AddScoped<IAuthDbContext>(sp => sp.GetRequiredService<AuthDbContext>());

        builder.Services
            .AddIdentityCore<ApplicationUser>()
            .AddRoles<ApplicationRole>()
            .AddEntityFrameworkStores<AuthDbContext>()
            .AddClaimsPrincipalFactory<ApplicationUserClaimsPrincipalFactory>()
            .AddDefaultTokenProviders();

        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<ITenantDatabaseModule, AuthTenantDatabase>();
    }

    /// <summary>
    /// The Auth services other modules depend on (reference data, caching, territory, roles, user
    /// accounts) without any tenant sign-in. The control plane uses this to seed tenants and manage
    /// their administrators; it never validates or issues tenant tokens.
    /// </summary>
    public static void AddAuthServices(this IHostApplicationBuilder builder)
    {
        builder.AddAuthPersistence();
        builder.Services.AddScoped<NWFM.Shared.Integration.Workflow.IWorkflowReferenceData, WorkflowReferenceData>();

        builder.Services.AddCachingServices(builder.Configuration);
        builder.Services.AddScoped<IPermissionResolver, PermissionResolver>();
        builder.Services.AddScoped<ITenantAvailability, SqlTenantAvailability>();
        builder.Services.AddScoped<IUserAccountService, UserAccountService>();

        // Territory for the modules that narrow work to it (Tasks), without referencing Auth.
        builder.Services.AddScoped<OrgScopeProvider>();
        builder.Services.AddScoped<IOrgScopeProvider>(sp => sp.GetRequiredService<OrgScopeProvider>());
        builder.Services.AddScoped<IOrgDirectory>(sp => sp.GetRequiredService<OrgScopeProvider>());
        builder.Services.AddScoped<IRoleLookup, RoleLookup>();

        builder.Services.Configure<SsoSettings>(builder.Configuration.GetSection(SsoSettings.SectionName));
        builder.Services.Configure<ActiveDirectorySettings>(builder.Configuration.GetSection(ActiveDirectorySettings.SectionName));
        builder.Services.Configure<TeamOtpOptions>(builder.Configuration.GetSection(TeamOtpOptions.SectionName));
        builder.Services.Configure<ExternalConsumersOptions>(builder.Configuration.GetSection(ExternalConsumersOptions.SectionName));
        builder.Services.Configure<ApiSecurityOptions>(builder.Configuration.GetSection(ApiSecurityOptions.SectionName));
        builder.Services.Configure<TeamGatewayOptions>(builder.Configuration.GetSection(TeamGatewayOptions.SectionName));
        builder.Services.AddSingleton<IActiveDirectoryAuthenticator, LdapActiveDirectoryAuthenticator>();
        builder.Services.AddScoped<ISmsSender, LoggingSmsSender>();
    }

    /// <summary>The tenant API: <see cref="AddAuthServices"/> plus tenant sign-in, token validation and permission policies.</summary>
    public static void AddAuthInfrastructure(this IHostApplicationBuilder builder)
    {
        builder.AddAuthServices();

        builder.Services.AddDataProtection();
        builder.Services.AddOptions<CookieAuthenticationOptions>(SsoDefaults.TempCookieScheme)
            .Configure<IDataProtectionProvider, ITenantContext>((options, provider, tenants) =>
                options.DataProtectionProvider = new TenantAwareDataProtector(provider.CreateProtector(
                    "NWFM.Auth.TenantCookies.v2", builder.Environment.EnvironmentName), tenants));

        builder.Services.AddScoped<IAuthService, AuthService>();
        builder.Services.AddScoped<TenantSessionValidator>();
        builder.Services.AddScoped<ISupportSessionTokenIssuer, SupportSessionTokenIssuer>();

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

                    // The default inbound map renames "tid" to the Entra tenant-id URI, which would hide
                    // the claim the request is bound by. Only that entry goes.
                    foreach (var handler in options.TokenHandlers.OfType<JsonWebTokenHandler>())
                        handler.InboundClaimTypeMap.Remove(TenantClaimTypes.TenantId);

                    options.Events = new JwtBearerEvents
                    {
                        OnTokenValidated = async context =>
                        {
                            // Bind the request to the token's tenant first: the session check below
                            // reads that tenant's own database.
                            if (context.HttpContext.RequestServices.GetService<ITenantSessionBinder>() is { } binder
                                && await binder.BindAsync(context.Principal!, context.HttpContext.RequestAborted) is { } refusal)
                            {
                                context.HttpContext.Items[TenantClaimTypes.TenantId + ".Refusal"] = refusal;
                                context.Fail(refusal.Message);
                                return;
                            }

                            var validator = context.HttpContext.RequestServices
                                .GetRequiredService<TenantSessionValidator>();
                            var principal = await validator.ValidateAsync(context.Principal!,
                                context.HttpContext.RequestAborted);
                            if (principal is null)
                            {
                                var logger = context.HttpContext.RequestServices
                                    .GetRequiredService<ILogger<TenantSessionValidator>>();
                                logger.LogWarning(new EventId(4101, "TenantSessionDenied"),
                                    "Tenant session rejected for tenant {TenantId}.",
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
}
