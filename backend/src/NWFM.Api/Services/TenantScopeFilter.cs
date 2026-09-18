namespace NWFM.Api.Services;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using NWFM.Shared.Abstractions;

/// <summary>Rejects explicit route, query, or body tenant overrides after model binding.</summary>
public sealed class TenantScopeFilter(ICurrentTenant tenant) : IActionFilter
{
    public void OnActionExecuting(ActionExecutingContext context)
    {
        foreach (var (key, value) in context.ActionArguments)
        {
            object? supplied = key.Equals("organizationId", StringComparison.OrdinalIgnoreCase) ? value
                : value?.GetType().GetProperty("OrganizationId")?.GetValue(value);
            if (supplied is Guid id && id != tenant.OrganizationId)
            {
                context.Result = new BadRequestObjectResult(new { code = "Tenant.Mismatch", message = "The requested tenant is not the active tenant." });
                return;
            }
        }
    }
    public void OnActionExecuted(ActionExecutedContext context) { }
}
