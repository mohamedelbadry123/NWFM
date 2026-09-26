using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using NWFM.Shared.Abstractions;
using NWFM.Shared.Caching;
using NWFM.Shared.Constants;
using NWFM.Shared.Exceptions;
using NWFM.Shared.Results;
using NWFM.Shared.Security;
using Tasks.Application.Common;
using Tasks.Application.Common.Interfaces;
using Tasks.Application.Constants;
using Tasks.Domain.Constants;
using Tasks.Domain.Entities;

namespace Tasks.Application.C2m;

public sealed record C2mActionMappingDto(
    Guid Id,
    string ActionCode,
    string FaStatus,
    string? CancelReason,
    string? ClosureReason,
    string NameEn,
    string NameAr,
    bool IsActive,
    DateTime UpdatedAt);

/// <summary>The C2M action mappings, for their admin screen.</summary>
[Authorize(Policy = NwfmPolicies.ManageTaskTypes)]
public sealed record GetC2mActionMappingsQuery : IRequest<Result<PaginatedResult<C2mActionMappingDto>>>
{
    public string? Search { get; init; }
    public int PageNumber { get; init; } = 1;
    public int PageSize { get; init; } = 10;
}

public sealed class GetC2mActionMappingsQueryValidator : AbstractValidator<GetC2mActionMappingsQuery>
{
    public GetC2mActionMappingsQueryValidator()
    {
        RuleFor(x => x.PageNumber).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 200);
    }
}

public sealed class GetC2mActionMappingsQueryHandler(ITasksDbContext db)
    : IRequestHandler<GetC2mActionMappingsQuery, Result<PaginatedResult<C2mActionMappingDto>>>
{
    public async Task<Result<PaginatedResult<C2mActionMappingDto>>> Handle(GetC2mActionMappingsQuery request, CancellationToken ct)
    {
        var query = db.C2mActionMappings.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim();
            query = query.Where(x => x.ActionCode.Contains(term) || x.NameEn.Contains(term) || x.NameAr.Contains(term));
        }

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderBy(x => x.ActionCode)
            .Skip((request.PageNumber - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(x => new C2mActionMappingDto(
                x.Id, x.ActionCode, x.FaStatus, x.CancelReason, x.ClosureReason, x.NameEn, x.NameAr, x.IsActive, x.UpdatedAt))
            .ToListAsync(ct);

        return Result.Success(new PaginatedResult<C2mActionMappingDto>(items, total, request.PageNumber, request.PageSize));
    }
}

/// <summary>What a create or an update carries.</summary>
public interface IC2mActionMappingInput
{
    string FaStatus { get; }
    string? CancelReason { get; }
    string? ClosureReason { get; }
    string NameEn { get; }
    string NameAr { get; }
}

public abstract class C2mActionMappingInputValidator<T> : AbstractValidator<T>
    where T : IC2mActionMappingInput
{
    protected C2mActionMappingInputValidator()
    {
        RuleFor(x => x.FaStatus)
            .NotEmpty()
            .Must(s => C2mOperationStatuses.IsDefined(s?.Trim().ToUpperInvariant()))
            .WithMessage($"FA status must be '{C2mOperationStatuses.Completed}' or '{C2mOperationStatuses.Cancelled}'.");

        // C2M rejects the reason that does not belong to the status, so a row carrying the wrong one is
        // caught here rather than at closure time, where nobody is watching.
        RuleFor(x => x.CancelReason)
            .Empty().WithMessage("A completed action must not carry a cancel reason.")
            .When(x => string.Equals(x.FaStatus?.Trim(), C2mOperationStatuses.Completed, StringComparison.OrdinalIgnoreCase));
        RuleFor(x => x.ClosureReason)
            .Empty().WithMessage("A cancelled action must not carry a closure reason.")
            .When(x => string.Equals(x.FaStatus?.Trim(), C2mOperationStatuses.Cancelled, StringComparison.OrdinalIgnoreCase));
        RuleFor(x => x.CancelReason).MaximumLength(C2mActionMapping.ReasonMaxLength);
        RuleFor(x => x.ClosureReason).MaximumLength(C2mActionMapping.ReasonMaxLength);
        RuleFor(x => x.NameEn).NotEmpty().MaximumLength(C2mActionMapping.NameMaxLength);
        RuleFor(x => x.NameAr).NotEmpty().MaximumLength(C2mActionMapping.NameMaxLength);
    }
}

[Authorize(Policy = NwfmPolicies.ManageTaskTypes)]
public sealed record CreateC2mActionMappingCommand : IRequest<Result<Guid>>, IC2mActionMappingInput
{
    public string ActionCode { get; init; } = default!;
    public string FaStatus { get; init; } = default!;
    public string? CancelReason { get; init; }
    public string? ClosureReason { get; init; }
    public string NameEn { get; init; } = default!;
    public string NameAr { get; init; } = default!;
    public bool IsActive { get; init; } = true;
}

public sealed class CreateC2mActionMappingCommandValidator : C2mActionMappingInputValidator<CreateC2mActionMappingCommand>
{
    public CreateC2mActionMappingCommandValidator() =>
        RuleFor(x => x.ActionCode).NotEmpty().MaximumLength(C2mActionMapping.CodeMaxLength);
}

public sealed class CreateC2mActionMappingCommandHandler(
    ITasksDbContext db,
    ICacheService cache,
    ICurrentUser user,
    TimeProvider clock)
    : IRequestHandler<CreateC2mActionMappingCommand, Result<Guid>>
{
    public async Task<Result<Guid>> Handle(CreateC2mActionMappingCommand request, CancellationToken ct)
    {
        var code = request.ActionCode.Trim().ToUpperInvariant();
        if (await db.C2mActionMappings.AnyAsync(x => x.ActionCode == code, ct))
        {
            return Result.Failure<Guid>(TaskErrors.C2m.MappingDuplicateCode(code));
        }

        C2mActionMapping mapping;
        try
        {
            mapping = C2mActionMapping.Create(
                code,
                request.FaStatus,
                request.CancelReason,
                request.ClosureReason,
                request.NameEn,
                request.NameAr,
                request.IsActive,
                TaskWrites.Actor(user),
                clock.GetUtcNow().UtcDateTime);
        }
        catch (DomainException ex)
        {
            return Result.Failure<Guid>(TaskErrors.Task.Invalid(ex.Message));
        }

        db.C2mActionMappings.Add(mapping);
        await db.SaveChangesAsync(ct);
        await cache.RemoveAsync(CacheKeys.Tasks.C2mActionMappings, ct);

        return Result.Success(mapping.Id);
    }
}

[Authorize(Policy = NwfmPolicies.ManageTaskTypes)]
public sealed record UpdateC2mActionMappingCommand : IRequest<Result>, IC2mActionMappingInput
{
    public Guid Id { get; init; }
    public string FaStatus { get; init; } = default!;
    public string? CancelReason { get; init; }
    public string? ClosureReason { get; init; }
    public string NameEn { get; init; } = default!;
    public string NameAr { get; init; } = default!;
    public bool IsActive { get; init; } = true;
}

public sealed class UpdateC2mActionMappingCommandValidator : C2mActionMappingInputValidator<UpdateC2mActionMappingCommand>
{
    public UpdateC2mActionMappingCommandValidator() => RuleFor(x => x.Id).NotEmpty();
}

public sealed class UpdateC2mActionMappingCommandHandler(
    ITasksDbContext db,
    ICacheService cache,
    ICurrentUser user,
    TimeProvider clock)
    : IRequestHandler<UpdateC2mActionMappingCommand, Result>
{
    public async Task<Result> Handle(UpdateC2mActionMappingCommand request, CancellationToken ct)
    {
        var mapping = await db.C2mActionMappings.FirstOrDefaultAsync(x => x.Id == request.Id, ct);
        if (mapping is null)
        {
            return Result.Failure(TaskErrors.C2m.MappingNotFound);
        }

        var result = await TaskWrites.ApplyAsync(
            db,
            () => mapping.Update(
                request.FaStatus,
                request.CancelReason,
                request.ClosureReason,
                request.NameEn,
                request.NameAr,
                request.IsActive,
                TaskWrites.Actor(user),
                clock.GetUtcNow().UtcDateTime),
            ct);

        if (result.IsSuccess)
        {
            await cache.RemoveAsync(CacheKeys.Tasks.C2mActionMappings, ct);
        }

        return result;
    }
}

[Authorize(Policy = NwfmPolicies.ManageTaskTypes)]
public sealed record SetC2mActionMappingStatusCommand(Guid Id, bool IsActive) : IRequest<Result>;

public sealed class SetC2mActionMappingStatusCommandHandler(
    ITasksDbContext db,
    ICacheService cache,
    ICurrentUser user,
    TimeProvider clock)
    : IRequestHandler<SetC2mActionMappingStatusCommand, Result>
{
    public async Task<Result> Handle(SetC2mActionMappingStatusCommand request, CancellationToken ct)
    {
        var mapping = await db.C2mActionMappings.FirstOrDefaultAsync(x => x.Id == request.Id, ct);
        if (mapping is null)
        {
            return Result.Failure(TaskErrors.C2m.MappingNotFound);
        }

        mapping.SetActive(request.IsActive, TaskWrites.Actor(user), clock.GetUtcNow().UtcDateTime);
        await db.SaveChangesAsync(ct);
        await cache.RemoveAsync(CacheKeys.Tasks.C2mActionMappings, ct);

        return Result.Success();
    }
}
