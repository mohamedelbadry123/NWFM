namespace Workflow.Api.Controllers;

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using NWFM.Shared.Abstractions;

public abstract class WorkflowControllerBase : ControllerBase
{
    protected (Guid OrganizationId, Guid ActorId, Guid DefaultActorId) Context =>
        (HttpContext.RequestServices.GetRequiredService<ICurrentTenant>().OrganizationId,
         HttpContext.RequestServices.GetRequiredService<IWorkflowActorContext>().ActorId,
         HttpContext.RequestServices.GetRequiredService<IWorkflowActorContext>().DefaultActorId);

    protected bool TryResolveTenant(out Guid id)
    {
        id = Context.OrganizationId;
        return id != Guid.Empty;
    }

    protected bool TryGetActorId(out Guid id)
    {
        id = Context.ActorId;
        return id != Guid.Empty;
    }
}
