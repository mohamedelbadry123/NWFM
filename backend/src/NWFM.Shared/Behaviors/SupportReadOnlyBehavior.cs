using System.Reflection;
using MediatR;
using NWFM.Shared.Abstractions;
using NWFM.Shared.Exceptions;
using NWFM.Shared.MultiTenancy;
using NWFM.Shared.Security;

namespace NWFM.Shared.Behaviors;

/// <summary>
/// Blocks writes when platform support is inside the tenant on a read-only grant. Queries still
/// run. Commands opt out only with <see cref="AllowSupportReadOnlyAttribute"/>.
/// </summary>
public sealed class SupportReadOnlyBehavior<TRequest, TResponse>(ICurrentUser user)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private const string WriteBlocked =
        "This support session is read-only. The workspace has not granted write access.";

    public Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (user.IsSupportSession
            && string.Equals(user.SupportScope, SupportScopes.ReadOnly, StringComparison.Ordinal)
            && IsWrite(typeof(TRequest)))
        {
            throw new DomainException(WriteBlocked, TenancyErrorCodes.SupportSessionReadOnly, 403);
        }

        return next();
    }

    private static bool IsWrite(Type type) =>
        type.Name.EndsWith("Command", StringComparison.Ordinal)
        && type.GetCustomAttribute<AllowSupportReadOnlyAttribute>() is null;
}
