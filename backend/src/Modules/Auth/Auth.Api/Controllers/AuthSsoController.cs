using System.Security.Claims;
using Auth.Application.Auth.Commands.Logout;
using Auth.Application.Auth.Commands.SsoSignIn;
using NWFM.Shared.Constants;
using Auth.Domain.Constants;
using Auth.Domain.Options;
using MediatR;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Auth.Api.Controllers;

[ApiController]
[Route("api/v1/auth/sso")]
[AllowAnonymous]
public sealed class AuthSsoController(ISender sender, IOptions<SsoSettings> ssoOptions) : ControllerBase
{
    private SsoSettings Settings => ssoOptions.Value;

    [HttpGet("login")]
    public IActionResult Login([FromQuery] string? returnUrl)
    {
        if (!Settings.Enabled) return NotFound();

        var properties = new AuthenticationProperties
        {
            RedirectUri = AppendReturnUrl(ResolveCallbackRedirect(), SanitizeReturnUrl(returnUrl)),
        };
        return Challenge(properties, SsoDefaults.Saml2Scheme);
    }

    [HttpGet("callback")]
    public async Task<IActionResult> Callback([FromQuery] string? returnUrl, CancellationToken ct)
    {
        if (!Settings.Enabled) return NotFound();

        var authentication = await HttpContext.AuthenticateAsync(SsoDefaults.TempCookieScheme);
        await HttpContext.SignOutAsync(SsoDefaults.TempCookieScheme);

        if (!authentication.Succeeded || authentication.Principal is null)
            return RedirectToAccessDenied(SsoDefaults.DenialReasons.NoNameId, null);

        var nameId = authentication.Principal.FindFirstValue(ClaimTypes.NameIdentifier)
                     ?? authentication.Principal.FindFirstValue(ClaimTypes.Name);

        if (string.IsNullOrWhiteSpace(nameId))
            return RedirectToAccessDenied(SsoDefaults.DenialReasons.NoNameId, null);

        var sessionIndex = authentication.Principal.FindFirstValue("urn:Sustainsys.Saml2:SessionIndex")
                           ?? authentication.Principal.FindFirstValue("SessionIndex");

        var result = await sender.Send(new SsoSignInCommand { NameId = nameId, SessionIndex = sessionIndex }, ct);

        if (!result.IsSuccess || result.Value is null)
            return RedirectToAccessDenied(SsoDefaults.DenialReasons.ExchangeFailed, nameId);

        if (!result.Value.IsGranted)
            return RedirectToAccessDenied(result.Value.DenialReason!, nameId);

        var target = AppendReturnUrl(
            $"{Settings.ClientAppBaseUrl.TrimEnd('/')}{SsoDefaults.ClientCallbackPath}?code={Uri.EscapeDataString(result.Value.AuthorizationCode!)}",
            SanitizeReturnUrl(returnUrl));

        return Redirect(target);
    }

    [HttpGet("logout")]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        if (!Settings.Enabled) return NotFound();
        if (User.Identity?.IsAuthenticated == true)
            await sender.Send(new LogoutCommand(), ct);
        await HttpContext.SignOutAsync(SsoDefaults.TempCookieScheme);
        return string.IsNullOrWhiteSpace(Settings.LogoutUrl)
            ? Redirect($"{Settings.ClientAppBaseUrl.TrimEnd('/')}/")
            : Redirect(Settings.LogoutUrl);
    }

    private string ResolveCallbackRedirect() =>
        !string.IsNullOrWhiteSpace(Settings.CallbackUrl)
            ? Settings.CallbackUrl
            : $"{HttpContext.Request.PathBase}{SsoDefaults.CallbackPath}";

    private static string? SanitizeReturnUrl(string? returnUrl) =>
        !string.IsNullOrWhiteSpace(returnUrl) && returnUrl.StartsWith('/') && !returnUrl.StartsWith("//")
            ? returnUrl : null;

    private IActionResult RedirectToAccessDenied(string reason, string? username)
    {
        var target = $"{Settings.ClientAppBaseUrl.TrimEnd('/')}{SsoDefaults.ClientAccessDeniedPath}?reason={Uri.EscapeDataString(reason)}";
        if (!string.IsNullOrWhiteSpace(username))
            target += $"&username={Uri.EscapeDataString(username)}";
        return Redirect(target);
    }

    private static string AppendReturnUrl(string url, string? returnUrl) =>
        string.IsNullOrWhiteSpace(returnUrl) ? url : $"{url}{(url.Contains('?') ? '&' : '?')}returnUrl={Uri.EscapeDataString(returnUrl)}";
}
