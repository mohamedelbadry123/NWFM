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
using Tasks.Application.Tasks.Common;
using Tasks.Domain.Constants;
using Tasks.Domain.Entities;

namespace Tasks.Application.Tasks.Commands.CreateTask;

/// <summary>
/// Raises a task. It pins its type's form at the version published now, so a later redesign never
/// changes the form under a crew already on site.
/// </summary>
[Authorize(Policy = NwfmPolicies.ManageTasks)]
public sealed record CreateTaskCommand : IRequest<Result<Guid>>, ITaskLocationInput
{
    public Guid TaskTypeId { get; init; }

    /// <summary>Leave blank to have one generated (<c>TSK-yyMMdd-XXXX</c>).</summary>
    public string? TaskNumber { get; init; }

    public string? Title { get; init; }
    public string? Notes { get; init; }
    public string Priority { get; init; } = TaskPriorities.Normal;
    public string? ExternalReference { get; init; }

    public double Latitude { get; init; }
    public double Longitude { get; init; }
    public string? Address { get; init; }
    public string? CbuCode { get; init; }
    public string? BranchCode { get; init; }
    public string? OperationAreaCode { get; init; }

    /// <summary>Defaults to the type's department.</summary>
    public string? DepartmentCode { get; init; }

    public DateTime? DueDate { get; init; }
    public DateTime? CompletionDueDate { get; init; }
}

public sealed class CreateTaskCommandValidator : AbstractValidator<CreateTaskCommand>
{
    public CreateTaskCommandValidator()
    {
        RuleFor(x => x.TaskTypeId).NotEmpty().WithMessage("Choose a task type.");
        RuleFor(x => x.TaskNumber).MaximumLength(FieldTask.TaskNumberMaxLength);
        RuleFor(x => x.Title).MaximumLength(FieldTask.TitleMaxLength);
        RuleFor(x => x.Notes).MaximumLength(FieldTask.NotesMaxLength);
        RuleFor(x => x.ExternalReference).MaximumLength(FieldTask.ExternalReferenceMaxLength);
        RuleFor(x => x.Priority).Must(TaskPriorities.IsDefined).WithMessage("Unknown task priority.");
        Include(new TaskLocationValidator());
    }
}

public sealed class CreateTaskCommandHandler(
    ITasksDbContext db,
    TaskAccess access,
    IFormGateway forms,
    ICurrentUser user,
    TimeProvider timeProvider)
    : IRequestHandler<CreateTaskCommand, Result<Guid>>
{
    public async Task<Result<Guid>> Handle(CreateTaskCommand request, CancellationToken ct)
    {
        var type = await db.TaskTypes.AsNoTracking().FirstOrDefaultAsync(t => t.Id == request.TaskTypeId, ct);
        if (type is null)
        {
            return Result.Failure<Guid>(TaskErrors.Type.NotFound);
        }

        if (!type.IsActive)
        {
            return Result.Failure<Guid>(TaskErrors.Type.Inactive);
        }

        var form = await forms.FindPublishedAsync(type.FormDefinitionId, ct);
        if (form is not { AcceptsSubmissions: true })
        {
            return Result.Failure<Guid>(TaskErrors.Form.NotPublished);
        }

        var departmentCode = string.IsNullOrWhiteSpace(request.DepartmentCode) ? type.DepartmentCode : request.DepartmentCode;

        // Raising work somewhere is claiming it for that territory; nobody may do that outside their own.
        if (!await access.CoversAsync(request.CbuCode, request.BranchCode, request.OperationAreaCode, departmentCode, ct))
        {
            return Result.Failure<Guid>(TaskErrors.Task.OutsideScope);
        }

        var numberResult = await TaskNumbers.ResolveAsync(db, request.TaskNumber, timeProvider, ct);
        if (numberResult.IsFailure)
        {
            return Result.Failure<Guid>(numberResult.Error);
        }

        FieldTask task;
        try
        {
            task = FieldTask.Create(
                new FieldTaskDraft
                {
                    TaskNumber = numberResult.Value,
                    TaskTypeId = type.Id,
                    FormDefinitionId = form.Id,
                    FormVersionNo = form.CurrentVersionNo,
                    Source = TaskSources.Manual,
                    Title = request.Title,
                    Notes = request.Notes,
                    Priority = request.Priority,
                    ExternalReference = request.ExternalReference,
                    Location = TaskLocationValidator.ToLocation(request, departmentCode),
                    DueDate = request.DueDate,
                    CompletionDueDate = request.CompletionDueDate,
                    FillSlaHours = type.FillSlaHours,
                    CompletionSlaHours = type.CompletionSlaHours,
                    CreatedBy = TaskWrites.Actor(user),
                },
                timeProvider.GetUtcNow().UtcDateTime);
        }
        catch (DomainException ex)
        {
            return Result.Failure<Guid>(TaskErrors.Task.Invalid(ex.Message));
        }

        db.Tasks.Add(task);
        await db.SaveChangesAsync(ct);

        return Result.Success(task.Id);
    }
}
