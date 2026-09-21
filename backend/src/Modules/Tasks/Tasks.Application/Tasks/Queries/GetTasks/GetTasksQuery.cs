using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using NWFM.Shared.Constants;
using NWFM.Shared.Integration.Forms;
using NWFM.Shared.Integration.Organization;
using NWFM.Shared.Results;
using NWFM.Shared.Security;
using Tasks.Application.Common;
using Tasks.Application.Common.Interfaces;
using Tasks.Application.Tasks.Common;
using Tasks.Application.Tasks.Models;
using Tasks.Domain.Constants;
using Tasks.Domain.Entities;

namespace Tasks.Application.Tasks.Queries.GetTasks;

/// <summary>The task worklist: the caller's tasks, filtered, sorted and paged on the server.</summary>
[Authorize(Policy = NwfmPolicies.ViewTasks)]
public sealed record GetTasksQuery : IRequest<Result<PaginatedResult<TaskListItemDto>>>
{
    public int PageNumber { get; init; } = 1;
    public int PageSize { get; init; } = 10;

    /// <summary>Task number, title or external reference.</summary>
    public string? Search { get; init; }

    public string[]? Statuses { get; init; }
    public Guid? TaskTypeId { get; init; }
    public string? Source { get; init; }
    public string? Priority { get; init; }

    /// <summary>Expanded to the CBUs beneath it: tasks carry no cluster of their own.</summary>
    public string? ClusterCode { get; init; }

    public string? CbuCode { get; init; }
    public string? BranchCode { get; init; }
    public string? OperationAreaCode { get; init; }
    public string? DepartmentCode { get; init; }

    /// <summary>Tasks this team holds now.</summary>
    public Guid? TeamId { get; init; }

    public string? ReturnReasonCode { get; init; }
    public DateTime? CreatedFrom { get; init; }
    public DateTime? CreatedTo { get; init; }
    public DateTime? DueFrom { get; init; }
    public DateTime? DueTo { get; init; }

    /// <summary>Only open tasks whose fill deadline has passed.</summary>
    public bool OverdueOnly { get; init; }

    /// <summary>One of <see cref="GetTasksQueryValidator.SortFields"/>; newest first by default.</summary>
    public string? SortField { get; init; }

    public bool SortDescending { get; init; } = true;
}

public sealed class GetTasksQueryValidator : AbstractValidator<GetTasksQuery>
{
    public const int MaxPageSize = 200;

    public static readonly IReadOnlyList<string> SortFields =
        ["taskNumber", "status", "priority", "createdAt", "updatedAt", "dueDate", "submittedDate", "submissionCount"];

    public GetTasksQueryValidator()
    {
        RuleFor(x => x.PageNumber).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, MaxPageSize);
        RuleFor(x => x.Search).MaximumLength(200);

        RuleForEach(x => x.Statuses)
            .Must(TaskStatuses.IsDefined).WithMessage("Unknown task status '{PropertyValue}'.");

        RuleFor(x => x.Source)
            .Must(TaskSources.IsDefined).When(x => !string.IsNullOrWhiteSpace(x.Source))
            .WithMessage("Unknown task source.");

        RuleFor(x => x.Priority)
            .Must(TaskPriorities.IsDefined).When(x => !string.IsNullOrWhiteSpace(x.Priority))
            .WithMessage("Unknown task priority.");

        RuleFor(x => x.SortField)
            .Must(field => SortFields.Contains(field!, StringComparer.OrdinalIgnoreCase))
            .When(x => !string.IsNullOrWhiteSpace(x.SortField))
            .WithMessage($"Sort by one of: {string.Join(", ", SortFields)}.");
    }
}

public sealed class GetTasksQueryHandler(
    ITasksDbContext db,
    TaskAccess access,
    IOrgScopeProvider scopes,
    IOrgDirectory directory,
    IFormGateway forms,
    TimeProvider timeProvider)
    : IRequestHandler<GetTasksQuery, Result<PaginatedResult<TaskListItemDto>>>
{
    public async Task<Result<PaginatedResult<TaskListItemDto>>> Handle(GetTasksQuery request, CancellationToken ct)
    {
        var query = await access.VisibleAsync(ct);
        query = await ApplyFiltersAsync(query, request, ct);

        var total = await query.CountAsync(ct);

        var page = await Sort(query, request)
            .Include(t => t.Assignments)
            .AsNoTracking()
            .Skip((request.PageNumber - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(ct);

        var items = await TaskProjection.ToListItemsAsync(db, directory, forms, page, ct);

        return Result.Success(new PaginatedResult<TaskListItemDto>(items, total, request.PageNumber, request.PageSize));
    }

    private async Task<IQueryable<FieldTask>> ApplyFiltersAsync(
        IQueryable<FieldTask> query,
        GetTasksQuery request,
        CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim();
            query = query.Where(t =>
                t.TaskNumber.Contains(term)
                || (t.Title != null && t.Title.Contains(term))
                || (t.ExternalReference != null && t.ExternalReference.Contains(term)));
        }

        if (request.Statuses is { Length: > 0 } statuses)
        {
            var list = statuses.ToList();
            query = query.Where(t => list.Contains(t.Status));
        }

        if (request.TaskTypeId is Guid typeId)
        {
            query = query.Where(t => t.TaskTypeId == typeId);
        }

        if (!string.IsNullOrWhiteSpace(request.Source))
        {
            query = query.Where(t => t.Source == request.Source);
        }

        if (!string.IsNullOrWhiteSpace(request.Priority))
        {
            query = query.Where(t => t.Priority == request.Priority);
        }

        if (!string.IsNullOrWhiteSpace(request.ClusterCode))
        {
            var hierarchy = await scopes.GetHierarchyAsync(ct);
            var cbus = hierarchy.CbusUnderCluster(request.ClusterCode).ToList();
            query = query.Where(t => t.CbuCode != null && cbus.Contains(t.CbuCode));
        }

        if (!string.IsNullOrWhiteSpace(request.CbuCode))
        {
            query = query.Where(t => t.CbuCode == request.CbuCode);
        }

        if (!string.IsNullOrWhiteSpace(request.BranchCode))
        {
            query = query.Where(t => t.BranchCode == request.BranchCode);
        }

        if (!string.IsNullOrWhiteSpace(request.OperationAreaCode))
        {
            query = query.Where(t => t.OperationAreaCode == request.OperationAreaCode);
        }

        if (!string.IsNullOrWhiteSpace(request.DepartmentCode))
        {
            query = query.Where(t => t.DepartmentCode == request.DepartmentCode);
        }

        if (request.TeamId is Guid teamId)
        {
            query = query.Where(t => t.Assignments.Any(a => a.IsActive && a.TeamId == teamId));
        }

        if (!string.IsNullOrWhiteSpace(request.ReturnReasonCode))
        {
            query = query.Where(t => t.ReturnReasonCode == request.ReturnReasonCode);
        }

        if (request.CreatedFrom is DateTime createdFrom)
        {
            query = query.Where(t => t.CreatedAt >= createdFrom);
        }

        if (request.CreatedTo is DateTime createdTo)
        {
            // A date picker sends midnight; "to the 5th" means through the end of the 5th.
            var end = createdTo.TimeOfDay == TimeSpan.Zero ? createdTo.AddDays(1) : createdTo;
            query = query.Where(t => t.CreatedAt < end);
        }

        if (request.DueFrom is DateTime dueFrom)
        {
            query = query.Where(t => t.DueDate >= dueFrom);
        }

        if (request.DueTo is DateTime dueTo)
        {
            var end = dueTo.TimeOfDay == TimeSpan.Zero ? dueTo.AddDays(1) : dueTo;
            query = query.Where(t => t.DueDate < end);
        }

        if (request.OverdueOnly)
        {
            var now = timeProvider.GetUtcNow().UtcDateTime;
            query = query.Where(t =>
                t.DueDate != null
                && t.DueDate < now
                && t.Status != TaskStatuses.Submitted
                && t.Status != TaskStatuses.Approved
                && t.Status != TaskStatuses.Expired);
        }

        return query;
    }

    /// <summary>Translates to a SQL CASE, so the rank is computed by the database.</summary>
    private static readonly System.Linq.Expressions.Expression<Func<FieldTask, int>> PriorityRank = t =>
        t.Priority == TaskPriorities.Urgent ? 4
        : t.Priority == TaskPriorities.High ? 3
        : t.Priority == TaskPriorities.Normal ? 2
        : 1;

    private static IQueryable<FieldTask> Sort(IQueryable<FieldTask> query, GetTasksQuery request)
    {
        var descending = request.SortDescending;

        // Id breaks ties, so a page boundary never falls between two rows the database may return in
        // either order — which would show one row twice and skip another.
        IOrderedQueryable<FieldTask> ordered = (request.SortField?.ToLowerInvariant()) switch
        {
            "tasknumber" => descending ? query.OrderByDescending(t => t.TaskNumber) : query.OrderBy(t => t.TaskNumber),
            "status" => descending ? query.OrderByDescending(t => t.Status) : query.OrderBy(t => t.Status),
            // By rank, not alphabetically — alphabetical would put HIGH before LOW before NORMAL.
            "priority" => descending ? query.OrderByDescending(PriorityRank) : query.OrderBy(PriorityRank),
            "updatedat" => descending ? query.OrderByDescending(t => t.UpdatedAt) : query.OrderBy(t => t.UpdatedAt),
            "duedate" => descending ? query.OrderByDescending(t => t.DueDate) : query.OrderBy(t => t.DueDate),
            "submitteddate" => descending ? query.OrderByDescending(t => t.SubmittedDate) : query.OrderBy(t => t.SubmittedDate),
            "submissioncount" => descending ? query.OrderByDescending(t => t.SubmissionCount) : query.OrderBy(t => t.SubmissionCount),
            _ => descending ? query.OrderByDescending(t => t.CreatedAt) : query.OrderBy(t => t.CreatedAt),
        };

        return ordered.ThenBy(t => t.Id);
    }
}
