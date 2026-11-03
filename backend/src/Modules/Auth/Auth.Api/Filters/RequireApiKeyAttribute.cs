using Auth.Domain.Options;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NWFM.Shared.Abstractions;
using NWFM.Shared.MultiTenancy;

namespace Auth.Api.Filters;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class RequireApiKeyAttribute(string scope) : Attribute, IAsyncAuthorizationFilter
{
    private const string ApiKeyHeaderName = "X-API-KEY";

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        if (!context.HttpContext.Request.Headers.TryGetValue(ApiKeyHeaderName, out var apiKeyHeader) ||
            apiKeyHeader.Count != 1)
        {
            context.Result = new UnauthorizedObjectResult("API key is required.");
            return;
        }

        var apiKey = apiKeyHeader.ToString();
        var options = context.HttpContext.RequestServices
            .GetRequiredService<IOptions<ExternalConsumersOptions>>().Value;
        var tenant = context.HttpContext.RequestServices.GetRequiredService<ICurrentTenant>();
        var environment = context.HttpContext.RequestServices.GetRequiredService<IHostEnvironment>().EnvironmentName;
        var active = context.HttpContext.RequestServices.GetRequiredService<ITenantContext>()
            .Current is { IsServing: true } current && current.Id == tenant.OrganizationId;

        var consumer = active ? options.FindAuthorizedConsumer(apiKey, tenant.OrganizationId, environment, scope) : null;
        if (consumer is null)
        {
            context.HttpContext.RequestServices.GetRequiredService<ILogger<RequireApiKeyAttribute>>()
                .LogWarning(new EventId(4103, "TenantServiceCredentialDenied"),
                    "Service credential rejected for deployment {TenantId}, request {TraceId}.",
                    tenant.OrganizationId, context.HttpContext.TraceIdentifier);
            context.Result = new UnauthorizedObjectResult("Invalid API key.");
            return;
        }

        context.HttpContext.Items["AuthorizedTenantService"] = consumer.AppID;
        await Task.CompletedTask;
    }
}
