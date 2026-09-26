using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Microsoft.Extensions.Options;
using Tasks.Application.C2m;

namespace Tasks.Infrastructure.C2m;

/// <summary>
/// C2M's closure endpoint over HTTP. Thin on purpose: the retries, the disabled switch and the
/// dispatch record belong to the dispatcher, where a failure has to be persisted.
/// </summary>
internal sealed class C2mHttpClient(HttpClient http, IOptions<C2mOptions> options) : IC2mClient
{
    public async Task<C2mClosureResponse> CloseFieldActivityAsync(C2mClosureRequest request, CancellationToken cancellationToken)
    {
        using var response = await http.PostAsJsonAsync(options.Value.CloseFieldActivityPath, request, cancellationToken);

        // C2M answers a refusal with a body describing it, sometimes under a 4xx; that body is the
        // reason worth recording. A response with no readable body is a transport problem.
        var body = await response.Content.ReadFromJsonAsync<C2mClosureResponse>(cancellationToken)
            ?? throw new HttpRequestException($"C2M answered {(int)response.StatusCode} with no body.");

        return body;
    }

    /// <summary>Base address, timeout and Basic credentials, from <see cref="C2mOptions"/>.</summary>
    public static void Configure(HttpClient client, C2mOptions settings)
    {
        if (Uri.TryCreate(settings.BaseUrl, UriKind.Absolute, out var baseUri))
        {
            client.BaseAddress = baseUri;
        }

        client.Timeout = TimeSpan.FromSeconds(Math.Max(settings.TimeoutSeconds, 1));

        if (!string.IsNullOrEmpty(settings.Username))
        {
            var token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{settings.Username}:{settings.Password}"));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", token);
        }
    }
}
