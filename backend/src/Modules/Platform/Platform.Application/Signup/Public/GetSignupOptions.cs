using Microsoft.Extensions.Options;
using NWFM.Shared.Options;
using Platform.Application.Common.Interfaces;
using Tenancy.Application.Common.Interfaces;

namespace Platform.Application.Signup.Public;

public sealed record SignupPlanDto
{
    public string Code { get; init; } = default!;

    public string NameEn { get; init; } = default!;

    public string NameAr { get; init; } = default!;

    public string? DescriptionEn { get; init; }

    public string? DescriptionAr { get; init; }

    public int? MaxUsers { get; init; }

    public int? MaxTeams { get; init; }

    public int? MaxStorageGb { get; init; }

    public int TrialDays { get; init; }
}

/// <summary>Everything the signup page needs before the customer types anything.</summary>
public sealed record SignupOptionsDto
{
    public IReadOnlyList<SignupPlanDto> Plans { get; init; } = [];

    public string DefaultPlanCode { get; init; } = default!;

    public string CaptchaProvider { get; init; } = default!;

    public string CaptchaSiteKey { get; init; } = default!;

    /// <summary>Workspace address pattern; <c>{key}</c> is the chosen key.</summary>
    public string WorkspaceUrlTemplate { get; init; } = default!;

    /// <summary>Addresses a bring-your-own database must accept connections from.</summary>
    public IReadOnlyList<string> EgressIps { get; init; } = [];
}

public sealed record GetSignupOptionsQuery : IRequest<Result<SignupOptionsDto>>;

public sealed class GetSignupOptionsQueryHandler(
    ICatalogDbContext catalog,
    ICaptchaVerifier captcha,
    IOptions<PlatformOptions> platformOptions)
    : IRequestHandler<GetSignupOptionsQuery, Result<SignupOptionsDto>>
{
    public async Task<Result<SignupOptionsDto>> Handle(GetSignupOptionsQuery request, CancellationToken cancellationToken)
    {
        var options = platformOptions.Value;

        var plans = await catalog.Plans
            .AsNoTracking()
            .Where(p => p.IsPublic && p.IsActive)
            .OrderBy(p => p.SortOrder)
            .Select(p => new SignupPlanDto
            {
                Code = p.Code,
                NameEn = p.NameEn,
                NameAr = p.NameAr,
                DescriptionEn = p.DescriptionEn,
                DescriptionAr = p.DescriptionAr,
                MaxUsers = p.MaxUsers,
                MaxTeams = p.MaxTeams,
                MaxStorageGb = p.MaxStorageGb,
                TrialDays = p.TrialDays
            })
            .ToListAsync(cancellationToken);

        var (provider, siteKey) = captcha.ClientSettings;

        return Result<SignupOptionsDto>.Success(new SignupOptionsDto
        {
            Plans = plans,
            DefaultPlanCode = options.DefaultPlanCode,
            CaptchaProvider = provider,
            CaptchaSiteKey = siteKey,
            WorkspaceUrlTemplate = options.TenantAppUrlTemplate,
            EgressIps = options.EgressIps
        });
    }
}
