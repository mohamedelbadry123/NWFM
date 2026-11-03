using System.Reflection;
using Platform.Application.Common.Security;

namespace Platform.Application.Common.Behaviors;

/// <summary>
/// Enforces <see cref="PlatformAuthorizeAttribute"/> against the signed-in platform staff member.
/// Registered on the control plane only; the endpoints carry the same policies, this is the second
/// line so a slice can never be reached by a caller its attribute does not allow.
/// </summary>
public sealed class PlatformAuthorizationBehavior<TRequest, TResponse>(IPlatformUser platformUser)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private const string PlatformAccessRequired = "Platform staff access is required.";

    public Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (request.GetType().GetCustomAttribute<PlatformAuthorizeAttribute>() is { } attribute)
        {
            if (!platformUser.IsAuthenticated)
            {
                throw new DomainException(PlatformAccessRequired, ApiErrorCodes.UnauthorizedAccess, 401);
            }

            var roles = attribute.Roles.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            if (roles.Length > 0 && !roles.Contains(platformUser.Role, StringComparer.Ordinal))
            {
                throw new DomainException(PlatformAccessRequired, ApiErrorCodes.UnauthorizedAccess, 403);
            }
        }

        return next();
    }
}
