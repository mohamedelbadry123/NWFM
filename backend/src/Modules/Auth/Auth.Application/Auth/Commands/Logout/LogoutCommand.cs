using MediatR;
using NWFM.Shared.Results;
using NWFM.Shared.Security;

namespace Auth.Application.Auth.Commands.Logout;

[Authorize]
public sealed record LogoutCommand(string? RefreshToken = null) : IRequest<Result<bool>>;
