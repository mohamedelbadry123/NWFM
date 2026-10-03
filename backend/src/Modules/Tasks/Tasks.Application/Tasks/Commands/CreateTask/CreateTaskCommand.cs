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
/// Raises a task. It pins its type's forms — and any extra forms asked for — each at the version
/// published now, so a later redesign never changes a form under a crew already on site.
/// </summary>
[Authorize(Policy = NwfmPolicies.ManageTasks)]
public sealed record CreateTaskCommand : IRequest<Result<Guid>>, ITaskLocationInput
{
    public Guid TaskTypeId { get; init; }

    /// <summary>Forms this task needs on top of its type's, filled by the same team; in order.</summary>
    public IReadOnlyList<Guid> ExtraFormDefinitionIds { get; init; } = [];

    /// <summary>Leave blank to have one generated (<c>TSK-yyMMdd-XXXX</c>).</summary>
    public string? TaskNumber { get; init; }

    public string? Title { get; init; }
    public string? Notes { get; init; }
    public string Priority { get; init; } = TaskPriorities.Normal;
    public string? ExternalReference { get; init; }

    /// <summary>The C2M field activity this task settles, when the work is C2M's.</summary>
    public string? FaId { get; init; }

    /// <summary>WFM's ticket for that activity.</summary>
    public long? WfmTicketId { get; init; }

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
        RuleFor(x => x.ExtraFormDefinitionIds)
            .Must(ids => ids is null || ids.Count < FieldTask.MaxForms)
            .WithMessage($"A task can carry at most {FieldTask.MaxForms} forms.")
            .Must(ids => ids is null || (ids.All(id => id != Guid.Empty) && ids.Distinct().Count() == ids.Count))
            .WithMessage("Each extra form can be added once.");
        RuleFor(x => x.TaskNumber).MaximumLength(FieldTask.TaskNumberMaxLength);
        RuleFor(x => x.Title).MaximumLength(FieldTask.TitleMaxLength);
        RuleFor(x => x.Notes).MaximumLength(FieldTask.NotesMaxLength);
        RuleFor(x => x.ExternalReference).MaximumLength(FieldTask.ExternalReferenceMaxLength);
        RuleFor(x => x.FaId).MaximumLength(FieldTask.FaIdMaxLength);
        RuleFor(x => x.WfmTicketId).GreaterThan(0).When(x => x.WfmTicketId is not null);
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
        var type = await db.TaskTypes.AsNoTracking().Include(t => t.Forms).FirstOrDefaultAsync(t => t.Id == request.TaskTypeId, ct);
        if (type is null)
        {
            return Result.Failure<Guid>(TaskErrors.Type.NotFound);
        }

        if (!type.IsActive)
        {
            return Result.Failure<Guid>(TaskErrors.Type.Inactive);
        }

        var typeForms = type.Forms.OrderBy(f => f.SortOrder).ToList();
        var extras = request.ExtraFormDefinitionIds ?? [];

        if (extras.Any(id => typeForms.Any(f => f.FormDefinitionId == id)))
        {
            return Result.Failure<Guid>(TaskErrors.Form.AlreadyOnTask);
        }

        // Every form is pinned at its version published now; one that takes no fills cannot be pinned.
        var drafts = new List<TaskFormDraft>(typeForms.Count + extras.Count);

        foreach (var (formId, source, closing) in typeForms
            .Select(f => (f.FormDefinitionId, TaskFormSources.Type, f.IsC2mClosingForm))
            .Concat(extras.Select(id => (id, TaskFormSources.Extra, false))))
        {
            var form = await forms.FindPublishedAsync(formId, ct);
            if (form is null)
            {
                return Result.Failure<Guid>(TaskErrors.Form.NotFound);
            }

            if (!form.AcceptsSubmissions)
            {
                return Result.Failure<Guid>(TaskErrors.Form.NotPublished);
            }

            drafts.Add(new TaskFormDraft(form.Id, form.CurrentVersionNo, source, closing));
        }

        var departmentCode = string.IsNullOrWhiteSpace(request.DepartmentCode) ? null : request.DepartmentCode;

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
                    Forms = drafts,
                    Source = TaskSources.Manual,
                    Title = request.Title,
                    Notes = request.Notes,
                    Priority = request.Priority,
                    ExternalReference = request.ExternalReference,
                    FaId = request.FaId,
                    WfmTicketId = request.WfmTicketId,
                    Location = TaskLocationValidator.ToLocation(request, departmentCode),
                    DueDate = request.DueDate,
                    CompletionDueDate = request.CompletionDueDate,
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
