using Platform.Application.Common.Security;
using Tenancy.Application.Common.Interfaces;
using Tenancy.Domain.Constants;

namespace Platform.Application.Plans;

public sealed record PlatformPlanDto
{
    public int Id { get; init; }

    public string Code { get; init; } = default!;

    public string NameEn { get; init; } = default!;

    public string NameAr { get; init; } = default!;

    public string? DescriptionEn { get; init; }

    public string? DescriptionAr { get; init; }

    public int? MaxUsers { get; init; }

    public int? MaxTeams { get; init; }

    public int? MaxStorageGb { get; init; }

    public int TrialDays { get; init; }

    public bool IsPublic { get; init; }

    public bool IsActive { get; init; }

    public int SortOrder { get; init; }

    public int TenantCount { get; init; }
}

[PlatformAuthorize]
public sealed record GetPlatformPlansQuery : IRequest<Result<IReadOnlyList<PlatformPlanDto>>>;

public sealed class GetPlatformPlansQueryHandler(ICatalogDbContext catalog)
    : IRequestHandler<GetPlatformPlansQuery, Result<IReadOnlyList<PlatformPlanDto>>>
{
    public async Task<Result<IReadOnlyList<PlatformPlanDto>>> Handle(GetPlatformPlansQuery request, CancellationToken cancellationToken)
    {
        var plans = await catalog.Plans
            .AsNoTracking()
            .OrderBy(p => p.SortOrder)
            .Select(p => new PlatformPlanDto
            {
                Id = p.Id,
                Code = p.Code,
                NameEn = p.NameEn,
                NameAr = p.NameAr,
                DescriptionEn = p.DescriptionEn,
                DescriptionAr = p.DescriptionAr,
                MaxUsers = p.MaxUsers,
                MaxTeams = p.MaxTeams,
                MaxStorageGb = p.MaxStorageGb,
                TrialDays = p.TrialDays,
                IsPublic = p.IsPublic,
                IsActive = p.IsActive,
                SortOrder = p.SortOrder,
                TenantCount = catalog.Subscriptions.Count(s => s.PlanId == p.Id)
            })
            .ToListAsync(cancellationToken);

        return Result<IReadOnlyList<PlatformPlanDto>>.Success(plans);
    }
}

/// <summary>Creates a plan (no <see cref="Id"/>) or updates one. The code is fixed once created.</summary>
[PlatformAuthorize(Roles = PlatformRoles.Admin)]
public sealed record SavePlatformPlanCommand : IRequest<Result<int>>
{
    public int? Id { get; init; }

    public string Code { get; init; } = default!;

    public string NameEn { get; init; } = default!;

    public string NameAr { get; init; } = default!;

    public string? DescriptionEn { get; init; }

    public string? DescriptionAr { get; init; }

    public int? MaxUsers { get; init; }

    public int? MaxTeams { get; init; }

    public int? MaxStorageGb { get; init; }

    public int TrialDays { get; init; }

    public bool IsPublic { get; init; }

    public bool IsActive { get; init; } = true;

    public int SortOrder { get; init; }
}

public sealed class SavePlatformPlanCommandValidator : AbstractValidator<SavePlatformPlanCommand>
{
    public SavePlatformPlanCommandValidator()
    {
        RuleFor(x => x.Code)
            .NotEmpty().WithMessage("Plan code is required.")
            .MaximumLength(Plan.CodeMaxLength).WithMessage($"Plan code must not exceed {Plan.CodeMaxLength} characters.")
            .Matches("^[a-z][a-z0-9-]*$").WithMessage("Plan code must be lowercase letters, digits or hyphens.");

        RuleFor(x => x.NameEn)
            .NotEmpty().WithMessage("English name is required.")
            .MaximumLength(Plan.NameMaxLength).WithMessage($"English name must not exceed {Plan.NameMaxLength} characters.");

        RuleFor(x => x.NameAr)
            .NotEmpty().WithMessage("Arabic name is required.")
            .MaximumLength(Plan.NameMaxLength).WithMessage($"Arabic name must not exceed {Plan.NameMaxLength} characters.");

        RuleFor(x => x.DescriptionEn)
            .MaximumLength(Plan.DescriptionMaxLength).WithMessage($"English description must not exceed {Plan.DescriptionMaxLength} characters.");

        RuleFor(x => x.DescriptionAr)
            .MaximumLength(Plan.DescriptionMaxLength).WithMessage($"Arabic description must not exceed {Plan.DescriptionMaxLength} characters.");

        RuleFor(x => x.MaxUsers).GreaterThan(0).WithMessage("User limit must be positive, or empty for unlimited.");
        RuleFor(x => x.MaxTeams).GreaterThan(0).WithMessage("Team limit must be positive, or empty for unlimited.");
        RuleFor(x => x.MaxStorageGb).GreaterThan(0).WithMessage("Storage limit must be positive, or empty for unlimited.");
        RuleFor(x => x.TrialDays).InclusiveBetween(0, 365).WithMessage("Trial days must be between 0 and 365.");
    }
}

public sealed class SavePlatformPlanCommandHandler(ICatalogDbContext catalog, ITenantStore tenantStore, IPlatformAuditWriter audit)
    : IRequestHandler<SavePlatformPlanCommand, Result<int>>
{
    public async Task<Result<int>> Handle(SavePlatformPlanCommand request, CancellationToken cancellationToken)
    {
        Plan plan;

        if (request.Id is { } id)
        {
            var existing = await catalog.Plans.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
            if (existing is null)
            {
                return PlatformResults.Fail<int>("Plan not found.", ApiErrorCodes.NotFound, httpStatusCode: 404);
            }

            plan = existing;
            plan.Update(request.NameEn, request.NameAr, request.DescriptionEn, request.DescriptionAr,
                request.MaxUsers, request.MaxTeams, request.MaxStorageGb, request.TrialDays, request.IsPublic, request.SortOrder);
            plan.SetActive(request.IsActive);
        }
        else
        {
            if (await catalog.Plans.AnyAsync(p => p.Code == request.Code, cancellationToken))
            {
                return PlatformResults.Fail<int>("A plan with this code already exists.", ApiErrorCodes.ValidationError, httpStatusCode: 409);
            }

            plan = Plan.Create(request.Code, request.NameEn, request.NameAr, request.DescriptionEn, request.DescriptionAr,
                request.MaxUsers, request.MaxTeams, request.MaxStorageGb, request.TrialDays, request.IsPublic, request.SortOrder);
            plan.SetActive(request.IsActive);
            catalog.Plans.Add(plan);
        }

        audit.Add(PlatformAuditActions.PlanSaved, null, nameof(Plan), request.Code);
        await catalog.SaveChangesAsync(cancellationToken);

        // Limits are carried on each tenant's snapshot entry.
        tenantStore.Invalidate();
        return Result<int>.Success(plan.Id);
    }
}
