using Auth.Application.Auth.Models;
using MediatR;
using NWFM.Shared.Results;

namespace Auth.Application.Auth.Queries.GetSsoStatus;

public sealed record GetSsoStatusQuery : IRequest<Result<SsoStatusDto>>;
