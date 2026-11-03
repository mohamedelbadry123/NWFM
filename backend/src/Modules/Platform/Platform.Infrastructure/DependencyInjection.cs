using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using NWFM.Shared.Options;
using Platform.Application.Common.Interfaces;
using Platform.Application.Common.Security;
using Platform.Infrastructure.Provisioning;
using Platform.Infrastructure.Security;
using Platform.Infrastructure.Services;
using Tenancy.Domain.Constants;

namespace Platform.Infrastructure;

/// <summary>
/// The control plane's own services: platform staff credentials and bearer scheme, provisioning,
/// tenant migrations and the startup initialiser. Registered by NWFM.Platform.Api only, on top of the
/// shared catalog runtime (<c>Tenancy.Infrastructure.DependencyInjection.AddTenancyRuntime</c>).
/// </summary>
public static class DependencyInjection
{
    private static readonly TimeSpan CaptchaTimeout = TimeSpan.FromSeconds(10);

    public static void AddPlatformInfrastructure(this IHostApplicationBuilder builder)
    {
        var services = builder.Services;
        var configuration = builder.Configuration;

        services.Configure<PlatformJwtSettings>(configuration.GetSection(PlatformJwtSettings.SectionName));
        services.Configure<EmailSettings>(configuration.GetSection(EmailSettings.SectionName));
        services.AddScoped<NWFM.Shared.Messaging.IEmailService, Messaging.SmtpEmailService>();

        services.AddSingleton<ISupportTenantDirectory, SupportTenantDirectory>();
        services.AddSingleton<ITenantAdministratorDirectory, TenantAdministratorDirectory>();
        services.AddSingleton<ITenantConnectionViewer, TenantConnectionViewer>();
        services.AddSingleton<ITenantDatabaseManager, PlatformTenantDatabaseManager>();
        services.AddSingleton<NWFM.Shared.MultiTenancy.TenantDatabaseManager>();
        services.AddSingleton<ITenantDatabaseVerification, TenantDatabaseVerification>();
        services.AddSingleton<PlatformWorkQueue>();
        services.AddSingleton<IPlatformWorkQueue>(sp => sp.GetRequiredService<PlatformWorkQueue>());
        services.AddHostedService<PlatformWorkWorker>();
        services.AddSingleton<IPasswordHasher<PlatformUser>, PasswordHasher<PlatformUser>>();
        services.AddSingleton<IPlatformCredentials, PlatformCredentials>();
        services.AddScoped<PlatformInitialiser>();
        services.AddScoped<ITenantMigrationRunner, TenantMigrationRunner>();

        AddSignupServices(services);

        services.AddAuthorization(options => options.AddPlatformPolicies());
        services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = PlatformAuthentication.Scheme;
                options.DefaultChallengeScheme = PlatformAuthentication.Scheme;
            })
            .AddPlatformJwt(configuration);
    }

    private static void AddSignupServices(IServiceCollection services)
    {
        services.AddHttpClient<ICaptchaVerifier, CaptchaVerifier>(client => client.Timeout = CaptchaTimeout);
        services.AddScoped<IClientIpAccessor, ClientIpAccessor>();
        services.AddSingleton<IByoDatabaseInspector, ByoDatabaseInspector>();
        services.AddScoped<ManagedDatabaseCreator>();
        services.AddSingleton<TenantAdministratorProvisioner>();
        services.AddScoped<ISignupProvisioner, SignupProvisioner>();
        services.AddScoped<ISignupProvisioningQueue, SignupProvisioningQueue>();
    }

    /// <summary>
    /// Adds the platform bearer scheme beside the tenant one. Separate key and audience, so neither
    /// token type validates under the other's scheme.
    /// </summary>
    public static AuthenticationBuilder AddPlatformJwt(this AuthenticationBuilder authentication, IConfiguration configuration)
    {
        var settings = configuration.GetSection(PlatformJwtSettings.SectionName).Get<PlatformJwtSettings>() ?? new PlatformJwtSettings();

        // The platform policies name this scheme, so it must exist even when unconfigured. A random
        // key then means no token validates, and platform sign-in refuses to issue one. A configured
        // key that is short or still the sample placeholder stops startup instead.
        if (!string.IsNullOrWhiteSpace(settings.SigningKey))
        {
            NWFM.Shared.Security.SigningKeyGuard.EnsureUsable(settings.SigningKey, $"{PlatformJwtSettings.SectionName}:SigningKey");
        }

        var signingKey = string.IsNullOrWhiteSpace(settings.SigningKey)
            ? System.Security.Cryptography.RandomNumberGenerator.GetBytes(64)
            : Encoding.UTF8.GetBytes(settings.SigningKey);

        return authentication.AddJwtBearer(PlatformAuthentication.Scheme, options =>
        {
            options.MapInboundClaims = false;
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = settings.Issuer,
                ValidAudience = settings.Audience,
                IssuerSigningKey = new SymmetricSecurityKey(signingKey),
                AuthenticationType = PlatformAuthentication.Scheme,
                RoleClaimType = PlatformAuthentication.RoleClaim,
                NameClaimType = PlatformAuthentication.DisplayNameClaim
            };
        });
    }

    public static void AddPlatformPolicies(this Microsoft.AspNetCore.Authorization.AuthorizationOptions options)
    {
        options.AddPolicy(PlatformPolicies.Admin, policy => policy
            .AddAuthenticationSchemes(PlatformAuthentication.Scheme)
            .RequireAuthenticatedUser()
            .RequireRole(PlatformRoles.Admin));

        options.AddPolicy(PlatformPolicies.Staff, policy => policy
            .AddAuthenticationSchemes(PlatformAuthentication.Scheme)
            .RequireAuthenticatedUser()
            .RequireRole(PlatformRoles.Admin, PlatformRoles.Support));
    }
}
