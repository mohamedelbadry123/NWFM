using Microsoft.AspNetCore.Http;
using NWFM.Shared.Http;
using Platform.Application.Common.Interfaces;

namespace Platform.Infrastructure.Services;

/// <summary>
/// The caller's address as the server saw it. Forwarded headers are honoured only through the
/// host's <c>ForwardedHeaders</c> middleware and its known-proxy list, never read raw here.
/// </summary>
internal sealed class ClientIpAccessor(IHttpContextAccessor httpContextAccessor) : IClientIpAccessor
{
    public string? ClientIp => httpContextAccessor.HttpContext is { } httpContext
        ? ClientIpResolver.Resolve(httpContext, trustForwardedHeader: false)
        : null;
}
