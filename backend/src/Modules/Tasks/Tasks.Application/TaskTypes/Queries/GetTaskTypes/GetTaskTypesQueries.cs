using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using NWFM.Shared.Constants;
using NWFM.Shared.Integration.Forms;
using NWFM.Shared.Results;
using NWFM.Shared.Security;
using Tasks.Application.Common.Interfaces;
using Tasks.Application.Constants;
using Tasks.Application.TaskTypes.Common;
using Tasks.Application.TaskTypes.Models;

namespace Tasks.Application.TaskTypes.Queries.GetTaskTypes;

[Authorize(Policy = NwfmPolicies.TaskTypeReaders)]
public sealed record GetTaskTypesQuery : IRequest<Result<PaginatedResult<TaskTypeDto>>>
{
    public int PageNumber { get; init; } = 1;
    public int PageSize { get; init; } = 10;
    public string? SearchTerm { get; init; }
    public bool? IsActive { get; init; }
}

public sealed class GetTaskTypesQueryValidator : AbstractValidator<GetTaskTypesQuery>
{
    public GetTaskTypesQueryValidator()
    {
        RuleFor(x => x.PageNumber).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 200);
        RuleFor(x => x.SearchTerm).MaximumLength(200);
    }
}

/// <summary>Active types, for the pickers that raise and filter tasks.</summary>
[Authorize(Policy = NwfmPolicies.TaskTypeReaders)]
public sealed record GetActiveTaskTypesQuery : IRequest<Result<IReadOnlyList<TaskTypeDto>>>;

[Authorize(Policy = NwfmPolicies.TaskTypeReaders)]
public sealed record GetTaskTypeByIdQuery(Guid TaskTypeId) : IRequest<Result<TaskTypeDto>>;

/// <summary>Published forms a type can be bound to.</summary>
[Authorize(Policy = NwfmPolicies.ManageTaskTypes)]
public sealed record GetTaskTypeFormOptionsQuery(string? Search) : IRequest<Result<IReadOnlyList<FormOptionDto>>>;

/// <summary>Published forms that can be added to a single task, on top of its type's.</summary>
[Authorize(Policy = NwfmPolicies.ManageTasks)]
public sealed record GetTaskFormOptionsQuery(string? Search) : IRequest<Result<IReadOnlyList<FormOptionDto>>>;

public sealed class GetTaskTypesQueryHandler(ITasksDbContext db, IFormGateway forms)
    : IRequestHandler<GetTaskTypesQuery, Result<PaginatedResult<TaskTypeDto>>>
{
    public async Task<Result<PaginatedResult<TaskTypeDto>>> Handle(GetTaskTypesQuery request, CancellationToken ct)
    {
        var query = db.TaskTypes.AsNoTracking().Include(t => t.Forms).AsQueryable();

        if (request.IsActive is bool isActive)
        {
            query = query.Where(t => t.IsActive == isActive);
        }

        if (!string.IsNullOrWhiteSpace(request.SearchTerm))
        {
            var term = request.SearchTerm.Trim();
            query = query.Where(t => t.Code.Contains(term) || t.NameEn.Contains(term) || t.NameAr.Contains(term));
        }

        var total = await query.CountAsync(ct);

        var page = await query
            .OrderBy(t => t.Code)
            .Skip((request.PageNumber - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(ct);

        var items = await TaskTypeProjection.ToDtosAsync(forms, page, ct);

        return Result.Success(new PaginatedResult<TaskTypeDto>(items, total, request.PageNumber, request.PageSize));
    }
}

public sealed class GetActiveTaskTypesQueryHandler(ITasksDbContext db, IFormGateway forms)
    : IRequestHandler<GetActiveTaskTypesQuery, Result<IReadOnlyList<TaskTypeDto>>>
{
    public async Task<Result<IReadOnlyList<TaskTypeDto>>> Handle(GetActiveTaskTypesQuery request, CancellationToken ct)
    {
        var types = await db.TaskTypes
            .AsNoTracking()
            .Include(t => t.Forms)
            .Where(t => t.IsActive)
            .OrderBy(t => t.Code)
            .ToListAsync(ct);

        return Result.Success(await TaskTypeProjection.ToDtosAsync(forms, types, ct));
    }
}

public sealed class GetTaskTypeByIdQueryHandler(ITasksDbContext db, IFormGateway forms)
    : IRequestHandler<GetTaskTypeByIdQuery, Result<TaskTypeDto>>
{
    public async Task<Result<TaskTypeDto>> Handle(GetTaskTypeByIdQuery request, CancellationToken ct)
    {
        var type = await db.TaskTypes.AsNoTracking().Include(t => t.Forms).FirstOrDefaultAsync(t => t.Id == request.TaskTypeId, ct);

        return type is null
            ? Result.Failure<TaskTypeDto>(TaskErrors.Type.NotFound)
            : Result.Success((await TaskTypeProjection.ToDtosAsync(forms, [type], ct))[0]);
    }
}

public sealed class GetTaskTypeFormOptionsQueryHandler(IFormGateway forms)
    : IRequestHandler<GetTaskTypeFormOptionsQuery, Result<IReadOnlyList<FormOptionDto>>>
{
    public async Task<Result<IReadOnlyList<FormOptionDto>>> Handle(GetTaskTypeFormOptionsQuery request, CancellationToken ct) =>
        Result.Success(await FormOptions.ListAsync(forms, request.Search, ct));
}

public sealed class GetTaskFormOptionsQueryHandler(IFormGateway forms)
    : IRequestHandler<GetTaskFormOptionsQuery, Result<IReadOnlyList<FormOptionDto>>>
{
    public async Task<Result<IReadOnlyList<FormOptionDto>>> Handle(GetTaskFormOptionsQuery request, CancellationToken ct) =>
        Result.Success(await FormOptions.ListAsync(forms, request.Search, ct));
}

internal static class FormOptions
{
    private const int MaxOptions = 200;

    /// <summary>Forms a task can be filled with: published, and taking fills.</summary>
    public static async Task<IReadOnlyList<FormOptionDto>> ListAsync(IFormGateway forms, string? search, CancellationToken ct)
    {
        var published = await forms.ListPublishedAsync(search, MaxOptions, ct);

        return published
            .Where(f => f.AcceptsSubmissions)
            .Select(f => new FormOptionDto(f.Id, f.Code, f.NameEn, f.NameAr, f.CurrentVersionNo))
            .ToList();
    }
}
