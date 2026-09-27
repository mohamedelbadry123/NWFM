using System.Security.Cryptography.X509Certificates;
using Auth.Domain.Constants;
using Auth.Domain.Options;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NWFM.Shared.Constants;
using Sustainsys.Saml2;
using Sustainsys.Saml2.AspNetCore2;
using Sustainsys.Saml2.Metadata;

namespace Auth.Infrastructure.Identity.Sso;

public static class SsoAuthenticationSetup
{
    public static AuthenticationBuilder AddSaml2Sso(
        this AuthenticationBuilder builder,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        SsoSettings sso = configuration.GetSection(SsoSettings.SectionName).Get<SsoSettings>() ?? new SsoSettings();
        if (!sso.Enabled || string.IsNullOrWhiteSpace(sso.EntityId))
            return builder;

        builder.AddCookie(SsoDefaults.TempCookieScheme, options =>
        {
            options.Cookie.Name = SsoDefaults.TempCookieName;
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = Microsoft.AspNetCore.Http.SameSiteMode.None;
            options.Cookie.SecurePolicy = Microsoft.AspNetCore.Http.CookieSecurePolicy.Always;
            options.ExpireTimeSpan = TimeSpan.FromMinutes(5);
        });

        builder.AddSaml2(SsoDefaults.Saml2Scheme, options =>
        {
            options.SignInScheme = SsoDefaults.TempCookieScheme;
            options.SPOptions.EntityId = new EntityId(sso.EntityId);
            options.SPOptions.ModulePath = sso.ModulePath;
            options.SPOptions.ReturnUrl = new Uri(sso.CallbackUrl);

            if (!string.IsNullOrWhiteSpace(sso.CertPfxFile) && File.Exists(sso.CertPfxFile))
            {
                var cert = new X509Certificate2(sso.CertPfxFile, sso.CertPfxPassword);
                options.SPOptions.ServiceCertificates.Add(new ServiceCertificate
                {
                    Certificate = cert,
                    Use = CertificateUse.Signing
                });
            }

            if (!string.IsNullOrWhiteSpace(sso.IdentityProviderEntityId))
            {
                var idp = new IdentityProvider(
                    new EntityId(sso.IdentityProviderEntityId),
                    options.SPOptions)
                {
                    AllowUnsolicitedAuthnResponse = true,
                    LoadMetadata = !string.IsNullOrWhiteSpace(sso.IdentityProviderMetadataUrl)
                };

                if (!string.IsNullOrWhiteSpace(sso.IdentityProviderMetadataUrl))
                    idp.MetadataLocation = sso.IdentityProviderMetadataUrl;

                options.IdentityProviders.Add(idp);
            }
        });

        return builder;
    }
}
