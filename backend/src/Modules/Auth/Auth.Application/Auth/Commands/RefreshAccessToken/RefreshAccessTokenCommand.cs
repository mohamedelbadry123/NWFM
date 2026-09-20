using Auth.Application.Auth.Models;
using MediatR;
using NWFM.Shared.Results;

namespace Auth.Application.Auth.Commands.RefreshAccessToken;

public sealed record RefreshAccessTokenCommand(string RefreshToken) : IRequest<Result<AuthTokenDto>>;
