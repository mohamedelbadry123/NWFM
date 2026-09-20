using Auth.Application.Common.Interfaces;
using Auth.Application.Users;
using Auth.Application.Users.Models;
using MediatR;
using NWFM.Shared.Results;

namespace Auth.Application.Users.Commands.UpdateUser;

public sealed class UpdateUserCommandHandler(
    IUserAccountService userAccountService,
    IAuthDbContext db)
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

        var scopesResult = await UserOrgScopeWriter.ReplaceAsync(db, request.UserId, request.Scopes, ct);
        if (!scopesResult.IsSuccess)
            return Result<UserDetailDto>.Failure(scopesResult.Error);

        return await UserDetailLoader.LoadAsync(userAccountService, db, request.UserId, ct);
    }
}
