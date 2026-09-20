using Auth.Application.Auth.Models;
using MediatR;
using NWFM.Shared.Results;

namespace Auth.Application.Auth.Commands.LoginUser;

public sealed record LoginUserCommand(string UserName, string Password) : IRequest<Result<AuthTokenDto>>;
