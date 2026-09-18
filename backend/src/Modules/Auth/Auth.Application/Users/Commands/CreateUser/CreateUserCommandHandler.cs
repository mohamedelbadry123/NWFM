using Auth.Application.Common.Interfaces;
using Auth.Application.Users;
using Auth.Application.Users.Models;
using MediatR;
using NWFM.Shared.Results;

namespace Auth.Application.Users.Commands.CreateUser;

public sealed class CreateUserCommandHandler(
    IUserAccountService userAccountService,
    IAuthDbContext db)
    : IRequestHandler<CreateUserCommand, Result<UserDetailDto>>
{
    public async Task<Result<UserDetailDto>> Handle(CreateUserCommand request, CancellationToken ct)
    {
        var createResult = await userAccountService.CreateUserAsync(new NewUserAccount
        {
            UserName = request.UserName,
            Email = request.Email,
            PhoneNumber = request.PhoneNumber,
            Password = request.Password,
            Roles = request.Roles
        }, ct);

        if (!createResult.IsSuccess)
            return Result<UserDetailDto>.Failure(createResult.Error);

        var scopesResult = await UserOrgScopeWriter.ReplaceAsync(db, createResult.Value, request.Scopes, ct);
        if (!scopesResult.IsSuccess)
            return Result<UserDetailDto>.Failure(scopesResult.Error);

        return await UserDetailLoader.LoadAsync(userAccountService, db, createResult.Value, ct);
    }
}
