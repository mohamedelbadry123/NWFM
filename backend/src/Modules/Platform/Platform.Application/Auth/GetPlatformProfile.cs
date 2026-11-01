using Platform.Application.Common.Security;
using Tenancy.Application.Common.Interfaces;

namespace Platform.Application.Auth;

[PlatformAuthorize]
public sealed record GetPlatformProfileQuery : IRequest<Result<PlatformProfileDto>>;

public sealed record PlatformProfileDto
{
    public Guid UserId { get; init; }

    public string Email { get; init; } = default!;

    public string DisplayName { get; init; } = default!;

    public string Role { get; init; } = default!;
}

public sealed class GetPlatformProfileQueryHandler(ICatalogDbContext catalog, IPlatformUser platformUser)
    : IRequestHandler<GetPlatformProfileQuery, Result<PlatformProfileDto>>
{
    public async Task<Result<PlatformProfileDto>> Handle(GetPlatformProfileQuery request, CancellationToken cancellationToken)
    {
        var profile = await catalog.PlatformUsers
            .AsNoTracking()
            .Where(x => x.Id == platformUser.Id && x.IsActive)
            .Select(x => new PlatformProfileDto { UserId = x.Id, Email = x.Email, DisplayName = x.DisplayName, Role = x.Role })
            .FirstOrDefaultAsync(cancellationToken);

        return profile is null
            ? PlatformResults.Fail<PlatformProfileDto>("Platform account not found.", ApiErrorCodes.UnauthorizedAccess, httpStatusCode: 401)
            : Result<PlatformProfileDto>.Success(profile);
    }
}
