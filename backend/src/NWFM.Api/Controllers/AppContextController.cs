namespace NWFM.Api.Controllers;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using NWFM.Api.Services;

public sealed record TenantDto(Guid Id, string Name);
public sealed record AppContextDto(TenantDto Tenant);

[ApiController]
[Route("api/app-context")]
public sealed class AppContextController(IOptions<ApplicationOptions> options) : ControllerBase
{
    [HttpGet]
    [Authorize]
    public ActionResult<AppContextDto> Get()
    {
        return new AppContextDto(new(options.Value.TenantId, options.Value.TenantName));
    }
}
