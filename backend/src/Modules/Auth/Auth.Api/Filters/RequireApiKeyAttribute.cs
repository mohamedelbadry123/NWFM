using Auth.Domain.Options;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Auth.Api.Filters;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class RequireApiKeyAttribute : Attribute, IAsyncAuthorizationFilter
{
    private const string ApiKeyHeaderName = "X-API-KEY";

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        if (!context.HttpContext.Request.Headers.TryGetValue(ApiKeyHeaderName, out var apiKeyHeader))
        {
            context.Result = new UnauthorizedObjectResult("API key is required.");
            return;
        }

        var apiKey = apiKeyHeader.ToString();
        var options = context.HttpContext.RequestServices
            .GetRequiredService<IOptions<ExternalConsumersOptions>>().Value;

        if (options.FindByApiKey(apiKey) is null)
        {
            context.Result = new UnauthorizedObjectResult("Invalid API key.");
            return;
        }

        await Task.CompletedTask;
    }
}
