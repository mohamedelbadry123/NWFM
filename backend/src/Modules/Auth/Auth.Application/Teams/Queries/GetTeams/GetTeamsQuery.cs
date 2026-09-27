using Auth.Application.Common.Interfaces;
using Auth.Application.Teams.Models;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using NWFM.Shared.Constants;
using NWFM.Shared.Results;
using NWFM.Shared.Security;

namespace Auth.Application.Teams.Queries.GetTeams;

[Authorize(Policy = NwfmPolicies.TeamReaders)]
public sealed record GetTeamsQuery : IRequest<Result<PaginatedResult<TeamDto>>>
{
    public int PageNumber { get; init; } = 1;
    public int PageSize { get; init; } = 10;
    public string? SearchTerm { get; init; }
    public bool? IsActive { get; init; }
}

public sealed class GetTeamsQueryValidator : AbstractValidator<GetTeamsQuery>
{
    public const int MaxPageSize = 500;

    public GetTeamsQueryValidator()
    {
        RuleFor(x => x.PageNumber).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, MaxPageSize);
        RuleFor(x => x.SearchTerm).MaximumLength(200);
    }
}

public sealed class GetTeamsQueryHandler(
    IAuthDbContext db,
    IUserAccountService accounts)
    : IRequestHandler<GetTeamsQuery, Result<PaginatedResult<TeamDto>>>
{
    public async Task<Result<PaginatedResult<TeamDto>>> Handle(GetTeamsQuery request, CancellationToken ct)
    {
        var query = db.Teams.AsNoTracking();

        if (request.IsActive is bool isActive)
            query = query.Where(t => t.IsActive == isActive);

        if (!string.IsNullOrWhiteSpace(request.SearchTerm))
        {
            var term = request.SearchTerm.Trim();
            query = query.Where(t => t.Name.Contains(term) || (t.Mobile != null && t.Mobile.Contains(term)));
        }

        var total = await query.CountAsync(ct);

        var teams = await query
            .OrderBy(t => t.Name)
            .Skip((request.PageNumber - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(ct);

        var items = await TeamReader.ToDtosAsync(db, accounts, teams, ct);

        return Result.Success(new PaginatedResult<TeamDto>(items, total, request.PageNumber, request.PageSize));
    }
}
