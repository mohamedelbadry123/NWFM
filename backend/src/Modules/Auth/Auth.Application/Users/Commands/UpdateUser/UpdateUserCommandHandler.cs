using Auth.Application.Common.Interfaces;
using Auth.Application.Users.Models;
using MediatR;
using NWFM.Shared.Results;

namespace Auth.Application.Users.Commands.UpdateUser;

public sealed class UpdateUserCommandHandler(IUserAccountService userAccountService)
    : IRequestHandler<UpdateUserCommand, Result<UserDetailDto>>
{
    public async Task<Result<UserDetailDto>> Handle(UpdateUserCommand request, CancellationToken ct)
    {
        var updateResult = await userAccountService.UpdateUserAsync(request.UserId, new EditedUserAccount
        {
            Email = request.Email,
            PhoneNumber = request.PhoneNumber,
            Roles = request.Roles
        }, ct);

        if (!updateResult.IsSuccess)
            return Result<UserDetailDto>.Failure(updateResult.Error);

        var getResult = await userAccountService.GetUserAsync(request.UserId, ct);
        if (!getResult.IsSuccess)
            return Result<UserDetailDto>.Failure(getResult.Error);

        var u = getResult.Value;
        return Result<UserDetailDto>.Success(new UserDetailDto
        {
            Id = u.Id, UserName = u.UserName, Email = u.Email, PhoneNumber = u.PhoneNumber,
            IsEnabled = u.IsEnabled, TeamId = u.TeamId, Roles = u.Roles
        });
    }
}
