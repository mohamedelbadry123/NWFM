using Auth.Application.Auth.Models;
using Auth.Application.Common.Interfaces;
using Auth.Application.Constants;
using NWFM.Shared.Constants;
using Auth.Domain.Constants;
using MediatR;
using Microsoft.EntityFrameworkCore;
using NWFM.Shared.Abstractions;
using NWFM.Shared.Results;

namespace Auth.Application.Auth.Queries.GetCurrentUserProfile;

internal sealed class GetCurrentUserProfileQueryHandler(
    ICurrentUser currentUser,
    IAuthDbContext dbContext)
    : IRequestHandler<GetCurrentUserProfileQuery, Result<CurrentUserProfileDto>>
{
    public async Task<Result<CurrentUserProfileDto>> Handle(
        GetCurrentUserProfileQuery request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(currentUser.Id))
            return Result<CurrentUserProfileDto>.Failure(AuthErrors.Unauthorized);

        bool isUnrestricted = currentUser.IsInRole(Roles.Administrator);

        List<AuthScopeDto> scopes = await dbContext.OrgScopes
            .Where(s => s.OwnerId == currentUser.Id
                        && s.OwnerType == OrgScopeOwnerTypes.User
                        && s.IsActive)
            .Select(s => new AuthScopeDto
            {
                ScopeId = s.Id,
                Level = s.Level,
                Code = s.Code,
                DepartmentId = s.DepartmentId
            })
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return Result<CurrentUserProfileDto>.Success(new CurrentUserProfileDto
        {
            UserId = currentUser.Id,
            UserName = currentUser.UserName ?? string.Empty,
            Email = currentUser.Email,
            Roles = currentUser.Roles,
            Permissions = currentUser.Permissions,
            TeamId = currentUser.TeamId,
            IsUnrestrictedScope = isUnrestricted,
            Scopes = scopes
        });
    }
}
