using Auth.Application.Common.Interfaces;
using Auth.Domain.Constants;
using MediatR;
using NWFM.Shared.Results;

namespace Auth.Application.Users.Queries.GetAssignableRoles;

public sealed class GetAssignableRolesQueryHandler(IUserAccountService userAccountService)
    : IRequestHandler<GetAssignableRolesQuery, Result<IReadOnlyList<string>>>
{
    public async Task<Result<IReadOnlyList<string>>> Handle(
        GetAssignableRolesQuery request, CancellationToken ct)
    {
        var result = await userAccountService.GetAssignableRolesAsync(ct);
        if (!result.IsSuccess)
            return result;

        IReadOnlyList<string> roles = [.. result.Value
            .Where(role => !string.Equals(role, Roles.FieldTeam, StringComparison.Ordinal))];
        return Result<IReadOnlyList<string>>.Success(roles);
    }
}
