using Auth.Application.Common.Interfaces;
using Auth.Application.Users;
using Auth.Application.Users.Models;
using MediatR;
using NWFM.Shared.Results;

namespace Auth.Application.Users.Queries.GetUserById;

public sealed class GetUserByIdQueryHandler(
    IUserAccountService userAccountService,
    IAuthDbContext db)
    : IRequestHandler<GetUserByIdQuery, Result<UserDetailDto>>
{
    public Task<Result<UserDetailDto>> Handle(GetUserByIdQuery request, CancellationToken ct) =>
        UserDetailLoader.LoadAsync(userAccountService, db, request.UserId, ct);
}
