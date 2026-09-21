using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using NWFM.Shared.Abstractions;
using NWFM.Shared.Constants;
using NWFM.Shared.Exceptions;
using NWFM.Shared.Integration.Forms;
using NWFM.Shared.Results;
using NWFM.Shared.Security;
using Tasks.Application.Common;
using Tasks.Application.Common.Interfaces;
using Tasks.Application.Constants;
using Tasks.Application.TaskTypes.Common;
using Tasks.Application.TaskTypes.Models;
using Tasks.Domain.Entities;

namespace Tasks.Application.TaskTypes.Commands.SaveTaskType;

/// <summary>The fields a task type is created and edited with.</summary>
public interface ITaskTypeInput
{
    string NameEn { get; }
    string NameAr { get; }
    string? DescriptionEn { get; }
    string? DescriptionAr { get; }
    Guid FormDefinitionId { get; }
    string? DepartmentCode { get; }
    int? FillSlaHours { get; }
    int? CompletionSlaHours { get; }
}

[Authorize(Policy = NwfmPolicies.ManageTaskTypes)]
public sealed record CreateTaskTypeCommand : IRequest<Result<TaskTypeDto>>, ITaskTypeInput
{
    public string Code { get; init; } = default!;
    public string NameEn { get; init; } = default!;
    public string NameAr { get; init; } = default!;
    public string? DescriptionEn { get; init; }
    public string? DescriptionAr { get; init; }
    public Guid FormDefinitionId { get; init; }
    public string? DepartmentCode { get; init; }
    public int? FillSlaHours { get; init; }
    public int? CompletionSlaHours { get; init; }
}

/// <summary>
/// Edits a task type. Pointing it at another form changes what new tasks are filled with; tasks
/// already raised keep the form and version they pinned.
/// </summary>
[Authorize(Policy = NwfmPolicies.ManageTaskTypes)]
public sealed record UpdateTaskTypeCommand : IRequest<Result<TaskTypeDto>>, ITaskTypeInput
{
    public Guid TaskTypeId { get; init; }
    public string NameEn { get; init; } = default!;
    public string NameAr { get; init; } = default!;
    public string? DescriptionEn { get; init; }
    public string? DescriptionAr { get; init; }
    public Guid FormDefinitionId { get; init; }
    public string? DepartmentCode { get; init; }
    public int? FillSlaHours { get; init; }
    public int? CompletionSlaHours { get; init; }
}

/// <summary>Deactivating a type stops new tasks of it; tasks already raised carry on.</summary>
[Authorize(Policy = NwfmPolicies.ManageTaskTypes)]
public sealed record SetTaskTypeStatusCommand : IRequest<Result<TaskTypeDto>>
{
    public Guid TaskTypeId { get; init; }
    public bool IsActive { get; init; }
}

public sealed class TaskTypeInputValidator : AbstractValidator<ITaskTypeInput>
{
    public TaskTypeInputValidator()
    {
        RuleFor(x => x.NameEn).NotEmpty().MaximumLength(TaskType.NameMaxLength);
        RuleFor(x => x.NameAr).NotEmpty().MaximumLength(TaskType.NameMaxLength);
        RuleFor(x => x.DescriptionEn).MaximumLength(TaskType.DescriptionMaxLength);
        RuleFor(x => x.DescriptionAr).MaximumLength(TaskType.DescriptionMaxLength);
        RuleFor(x => x.FormDefinitionId).NotEmpty().WithMessage("Choose the form its tasks are filled with.");
        RuleFor(x => x.DepartmentCode).MaximumLength(TaskType.DepartmentCodeMaxLength);
        RuleFor(x => x.FillSlaHours).InclusiveBetween(1, TaskType.MaxSlaHours).When(x => x.FillSlaHours is not null);
        RuleFor(x => x.CompletionSlaHours).InclusiveBetween(1, TaskType.MaxSlaHours).When(x => x.CompletionSlaHours is not null);
    }
}

public sealed class CreateTaskTypeCommandValidator : AbstractValidator<CreateTaskTypeCommand>
{
    public CreateTaskTypeCommandValidator()
    {
        RuleFor(x => x.Code)
            .NotEmpty()
            .MaximumLength(TaskType.CodeMaxLength)
            .Matches("^[A-Za-z0-9_-]+$").WithMessage("A code may hold letters, digits, '-' and '_' only.");
        Include(new TaskTypeInputValidator());
    }
}

public sealed class UpdateTaskTypeCommandValidator : AbstractValidator<UpdateTaskTypeCommand>
{
    public UpdateTaskTypeCommandValidator()
    {
        RuleFor(x => x.TaskTypeId).NotEmpty();
        Include(new TaskTypeInputValidator());
    }
}

public sealed class CreateTaskTypeCommandHandler(
    ITasksDbContext db,
    IFormGateway forms,
    ICurrentUser user,
    TimeProvider timeProvider)
    : IRequestHandler<CreateTaskTypeCommand, Result<TaskTypeDto>>
{
    public async Task<Result<TaskTypeDto>> Handle(CreateTaskTypeCommand request, CancellationToken ct)
    {
        var code = request.Code.Trim();

        if (await db.TaskTypes.AnyAsync(t => t.Code == code, ct))
        {
            return Result.Failure<TaskTypeDto>(TaskErrors.Type.DuplicateCode(code));
        }

        if (await TaskTypeForms.RequirePublishedAsync(forms, request.FormDefinitionId, ct) is { } formError)
        {
            return Result.Failure<TaskTypeDto>(formError);
        }

        TaskType type;
        try
        {
            type = TaskType.Create(
                code,
                request.NameEn,
                request.NameAr,
                request.DescriptionEn,
                request.DescriptionAr,
                request.FormDefinitionId,
                request.DepartmentCode,
                request.FillSlaHours,
                request.CompletionSlaHours,
                TaskWrites.Actor(user),
                timeProvider.GetUtcNow().UtcDateTime);
        }
        catch (DomainException ex)
        {
            return Result.Failure<TaskTypeDto>(TaskErrors.Type.Invalid(ex.Message));
        }

        db.TaskTypes.Add(type);
        await db.SaveChangesAsync(ct);

        return Result.Success((await TaskTypeProjection.ToDtosAsync(forms, [type], ct))[0]);
    }
}

public sealed class UpdateTaskTypeCommandHandler(
    ITasksDbContext db,
    IFormGateway forms,
    ICurrentUser user,
    TimeProvider timeProvider)
    : IRequestHandler<UpdateTaskTypeCommand, Result<TaskTypeDto>>
{
    public async Task<Result<TaskTypeDto>> Handle(UpdateTaskTypeCommand request, CancellationToken ct)
    {
        var type = await db.TaskTypes.FirstOrDefaultAsync(t => t.Id == request.TaskTypeId, ct);
        if (type is null)
        {
            return Result.Failure<TaskTypeDto>(TaskErrors.Type.NotFound);
        }

        // A form the type already uses may have been deprecated since; that must not block editing
        // the type's names. Only a change of form has to point at one that takes fills.
        if (request.FormDefinitionId != type.FormDefinitionId
            && await TaskTypeForms.RequirePublishedAsync(forms, request.FormDefinitionId, ct) is { } formError)
        {
            return Result.Failure<TaskTypeDto>(formError);
        }

        try
        {
            type.Update(
                request.NameEn,
                request.NameAr,
                request.DescriptionEn,
                request.DescriptionAr,
                request.FormDefinitionId,
                request.DepartmentCode,
                request.FillSlaHours,
                request.CompletionSlaHours,
                TaskWrites.Actor(user),
                timeProvider.GetUtcNow().UtcDateTime);

            await db.SaveChangesAsync(ct);
        }
        catch (DomainException ex)
        {
            return Result.Failure<TaskTypeDto>(TaskErrors.Type.Invalid(ex.Message));
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure<TaskTypeDto>(TaskErrors.Task.ConcurrencyConflict);
        }

        return Result.Success((await TaskTypeProjection.ToDtosAsync(forms, [type], ct))[0]);
    }
}

public sealed class SetTaskTypeStatusCommandHandler(
    ITasksDbContext db,
    IFormGateway forms,
    ICurrentUser user,
    TimeProvider timeProvider)
    : IRequestHandler<SetTaskTypeStatusCommand, Result<TaskTypeDto>>
{
    public async Task<Result<TaskTypeDto>> Handle(SetTaskTypeStatusCommand request, CancellationToken ct)
    {
        var type = await db.TaskTypes.FirstOrDefaultAsync(t => t.Id == request.TaskTypeId, ct);
        if (type is null)
        {
            return Result.Failure<TaskTypeDto>(TaskErrors.Type.NotFound);
        }

        type.SetActive(request.IsActive, TaskWrites.Actor(user), timeProvider.GetUtcNow().UtcDateTime);
        await db.SaveChangesAsync(ct);

        return Result.Success((await TaskTypeProjection.ToDtosAsync(forms, [type], ct))[0]);
    }
}

internal static class TaskTypeForms
{
    /// <summary>A type is bound to a form a task can be filled with — one with a published version that takes fills.</summary>
    public static async Task<Error?> RequirePublishedAsync(IFormGateway forms, Guid formId, CancellationToken ct)
    {
        var form = await forms.FindPublishedAsync(formId, ct);

        return form switch
        {
            null => TaskErrors.Form.NotFound,
            { AcceptsSubmissions: false } => TaskErrors.Form.NotPublished,
            _ => null,
        };
    }
}
