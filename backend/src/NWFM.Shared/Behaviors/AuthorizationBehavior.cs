using System.Reflection;
using MediatR;
using NWFM.Shared.Abstractions;
using NWFM.Shared.Security;

namespace NWFM.Shared.Behaviors;

public sealed class AuthorizationBehavior<TRequest, TResponse>(ICurrentUser user)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private const string AnyPrefix = "Permissions.Any:";

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var authorizeAttributes = request.GetType().GetCustomAttributes<AuthorizeAttribute>();

        if (!authorizeAttributes.Any())
        {
            return await next();
        }

        if (user.Id is null)
        {
            throw new UnauthorizedAccessException();
        }

        foreach (var attr in authorizeAttributes.Where(a => !string.IsNullOrWhiteSpace(a.Roles)))
        {
            var authorized = attr.Roles.Split(',').Any(role => user.IsInRole(role.Trim()));
            if (!authorized)
            {
                throw new UnauthorizedAccessException("Forbidden.");
            }
        }

        if (user.IsInRole("Administrator"))
        {
            return await next();
        }

        foreach (var attr in authorizeAttributes.Where(a => !string.IsNullOrWhiteSpace(a.Policy)))
        {
            if (attr.Policy.StartsWith(AnyPrefix, StringComparison.OrdinalIgnoreCase))
            {
                var codes = attr.Policy[AnyPrefix.Length..]
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (!codes.Any(user.HasPermission))
                {
                    throw new UnauthorizedAccessException("Forbidden.");
                }
            }
            else if (!user.HasPermission(attr.Policy))
            {
                throw new UnauthorizedAccessException("Forbidden.");
            }
        }

        return await next();
    }
}
