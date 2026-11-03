using Microsoft.Extensions.Options;
using NWFM.Shared.Options;
using Platform.Application.Signup.Common;
using Tenancy.Application.Common.Interfaces;

namespace Platform.Application.Signup.Public;

public sealed record TenantKeyAvailabilityDto(string Key, bool IsAvailable, string? Reason);

public sealed record CheckTenantKeyQuery(string Key) : IRequest<Result<TenantKeyAvailabilityDto>>;

public sealed class CheckTenantKeyQueryValidator : AbstractValidator<CheckTenantKeyQuery>
{
    public CheckTenantKeyQueryValidator()
    {
        RuleFor(x => x.Key)
            .NotEmpty().WithMessage("Workspace address is required.")
            .MaximumLength(TenantKey.MaxLength).WithMessage($"Workspace address must not exceed {TenantKey.MaxLength} characters.");
    }
}

public sealed class CheckTenantKeyQueryHandler(
    ICatalogDbContext catalog,
    IOptions<TenancyOptions> tenancyOptions,
    TimeProvider timeProvider)
    : IRequestHandler<CheckTenantKeyQuery, Result<TenantKeyAvailabilityDto>>
{
    public async Task<Result<TenantKeyAvailabilityDto>> Handle(CheckTenantKeyQuery request, CancellationToken cancellationToken)
    {
        var key = TenantKey.Normalize(request.Key);
        var reason = await SignupKeyAvailability.UnavailableReasonAsync(
            catalog, tenancyOptions.Value, key, timeProvider.GetUtcNow().UtcDateTime, cancellationToken);

        return Result<TenantKeyAvailabilityDto>.Success(new TenantKeyAvailabilityDto(key, reason is null, reason));
    }
}
