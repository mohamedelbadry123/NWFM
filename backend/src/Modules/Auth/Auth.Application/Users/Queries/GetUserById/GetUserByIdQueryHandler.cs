using Auth.Application.Common.Interfaces;
using Auth.Application.Users.Models;
using MediatR;
using NWFM.Shared.Results;

namespace Auth.Application.Users.Queries.GetUserById;

public sealed class GetUserByIdQueryHandler(IUserAccountService userAccountService)
    : IRequestHandler<GetUserByIdQuery, Result<UserDetailDto>>
{
    private static readonly Error NotFound = new("Auth.NotFound", "User not found.");

    public async Task<Result<UserDetailDto>> Handle(GetUserByIdQuery request, CancellationToken ct)
    {
        var result = await userAccountService.GetUserAsync(request.UserId, ct);
        if (!result.IsSuccess)
            return Result<UserDetailDto>.Failure(NotFound);

        var u = result.Value;
        return Result<UserDetailDto>.Success(new UserDetailDto
        {
            Id = u.Id, UserName = u.UserName, Email = u.Email, PhoneNumber = u.PhoneNumber,
            IsEnabled = u.IsEnabled, TeamId = u.TeamId, Roles = u.Roles
        });
    }
}
