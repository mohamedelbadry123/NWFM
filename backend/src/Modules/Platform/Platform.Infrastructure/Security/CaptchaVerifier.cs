using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NWFM.Shared.Options;
using Platform.Application.Common.Interfaces;

namespace Platform.Infrastructure.Security;

/// <summary>
/// Cloudflare Turnstile, or nothing when <see cref="CaptchaProviders.None"/>. A verification that
/// cannot reach the provider fails closed: an outage there must not open the signup form to bots.
/// </summary>
internal sealed class CaptchaVerifier(
    HttpClient httpClient,
    IOptions<PlatformOptions> platformOptions,
    ILogger<CaptchaVerifier> logger)
    : ICaptchaVerifier
{
    private const string SecretField = "secret";
    private const string ResponseField = "response";
    private const string RemoteIpField = "remoteip";

    private CaptchaOptions Options => platformOptions.Value.Captcha;

    public (string Provider, string SiteKey) ClientSettings => (Options.Provider, Options.SiteKey);

    public async Task<bool> VerifyAsync(string? token, string? remoteIp, CancellationToken cancellationToken)
    {
        var options = Options;

        if (string.Equals(options.Provider, CaptchaProviders.None, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!string.Equals(options.Provider, CaptchaProviders.Turnstile, StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(options.SecretKey))
        {
            logger.LogError("Platform:Captcha is misconfigured (provider {Provider}); rejecting the signup.", options.Provider);
            return false;
        }

        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        var form = new Dictionary<string, string>
        {
            [SecretField] = options.SecretKey,
            [ResponseField] = token
        };

        if (!string.IsNullOrWhiteSpace(remoteIp))
        {
            form[RemoteIpField] = remoteIp;
        }

        try
        {
            using var response = await httpClient.PostAsync(options.VerifyUrl, new FormUrlEncodedContent(form), cancellationToken);
            response.EnsureSuccessStatusCode();

            var body = await response.Content.ReadFromJsonAsync<TurnstileResponse>(cancellationToken);
            return body?.Success == true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "CAPTCHA verification could not be completed; rejecting the signup.");
            return false;
        }
    }

    private sealed record TurnstileResponse([property: JsonPropertyName("success")] bool Success);
}
