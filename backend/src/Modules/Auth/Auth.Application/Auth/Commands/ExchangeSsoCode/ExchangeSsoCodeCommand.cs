using Auth.Application.Auth.Models;
using MediatR;
using NWFM.Shared.Results;

namespace Auth.Application.Auth.Commands.ExchangeSsoCode;

public sealed record ExchangeSsoCodeCommand(string Code) : IRequest<Result<AuthTokenDto>>;
