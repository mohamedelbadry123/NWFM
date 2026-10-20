namespace NWFM.Api.Services;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using NWFM.Shared.Abstractions;

/// <summary>Rejects explicit route, query, or body tenant overrides after model binding.</summary>
public sealed class TenantScopeFilter(ICurrentTenant tenant, ILogger<TenantScopeFilter> logger) : IActionFilter
{
    public void OnActionExecuting(ActionExecutingContext context)
    {
        foreach (var (key, value) in context.ActionArguments)
        {
            var supplied = IsTenantSelector(key)
                ? new[] { value }
                : value?.GetType().GetProperties()
                    .Where(p => IsTenantSelector(p.Name) && p.GetIndexParameters().Length == 0)
                    .Select(p => p.GetValue(value)) ?? [];
            if (tenant.OrganizationId == Guid.Empty ||
                supplied.Any(id => id is not null &&
                    (!Guid.TryParse(id.ToString(), out var parsed) || parsed != tenant.OrganizationId)))
            {
                context.HttpContext.Items["TenantSecurity.SelectorDenied"] = true;
                logger.LogWarning(new EventId(4102, "TenantSelectorDenied"),
                    "Tenant selector rejected for deployment {TenantId}, request {TraceId}.",
                    tenant.OrganizationId, context.HttpContext.TraceIdentifier);
                context.Result = new BadRequestObjectResult(new { code = "Tenant.Mismatch", message = "The requested tenant is not the active tenant." });
                return;
            }
        }
    }
    public void OnActionExecuted(ActionExecutedContext context) { }

    private static bool IsTenantSelector(string name) =>
        name.Equals("OrganizationId", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("TenantId", StringComparison.OrdinalIgnoreCase);
}
