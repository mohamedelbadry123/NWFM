using Auth.Application.Auth.Models;
using NWFM.Shared.Results;

namespace Auth.Application.Common.Interfaces;

public interface IAuthService
{
    Task<Result<AuthTokenDto>> LoginAsync(string userName, string password, CancellationToken ct);

    Task<Result<TeamLoginContext>> AuthenticateTeamAsync(string userCode, string password, CancellationToken ct);

    Task<Result<AuthTokenDto>> IssueTeamTokensAsync(string userId, CancellationToken ct);

    Task<Result<SsoSignInResultDto>> SignInWithSamlAsync(string nameId, string? sessionIndex, CancellationToken ct);

    Task<Result<AuthTokenDto>> ExchangeSsoCodeAsync(string code, CancellationToken ct);

    Task<Result<AuthTokenDto>> RefreshAsync(string refreshToken, CancellationToken ct);

    Task<Result<bool>> LogoutAsync(string userId, string? refreshToken, CancellationToken ct);
}
