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
using Tasks.Application.TaskTypes.Commands.SaveTaskType;
using Tasks.Application.TaskTypes.Common;
using Tasks.Application.TaskTypes.Models;

namespace Tasks.Application.TaskTypes.Commands.AttachTaskTypeForm;

/// <summary>
/// Files one published form under a task type without touching the type's other forms. The caller must
/// be allowed to manage both forms and task types: reading either is not enough, because this changes
/// what every new task of the type is filled with. Repeating it for a form the type already has succeeds
/// and changes nothing.
/// </summary>
[Authorize(Policy = NwfmPolicies.ManageForms)]
[Authorize(Policy = NwfmPolicies.ManageTaskTypes)]
public sealed record AttachTaskTypeFormCommand(Guid TaskTypeId, Guid FormDefinitionId) : IRequest<Result<TaskTypeDto>>;

public sealed class AttachTaskTypeFormCommandValidator : AbstractValidator<AttachTaskTypeFormCommand>
{
    public AttachTaskTypeFormCommandValidator()
    {
        RuleFor(x => x.TaskTypeId).NotEmpty();
        RuleFor(x => x.FormDefinitionId).NotEmpty();
    }
}

public sealed class AttachTaskTypeFormCommandHandler(
    ITasksDbContext db,
    IFormGateway forms,
    ICurrentUser user,
    TimeProvider timeProvider)
    : IRequestHandler<AttachTaskTypeFormCommand, Result<TaskTypeDto>>
{
    /// <summary>Concurrent writers re-read the type and try again; the same form arriving twice then finds itself already attached.</summary>
    private const int Attempts = 3;

    public async Task<Result<TaskTypeDto>> Handle(AttachTaskTypeFormCommand request, CancellationToken ct)
    {
        // The form is checked first and on every call: a form that stopped taking fills is not attached again.
        if (await TaskTypeForms.RequirePublishedAsync(forms, request.FormDefinitionId, ct) is { } formError)
        {
            return Result.Failure<TaskTypeDto>(formError);
        }

        for (var attempt = 1; ; attempt++)
        {
            var type = await db.TaskTypes.Include(t => t.Forms).FirstOrDefaultAsync(t => t.Id == request.TaskTypeId, ct);
            if (type is null)
            {
                return Result.Failure<TaskTypeDto>(TaskErrors.Type.NotFound);
            }

            if (!type.IsActive)
            {
                return Result.Failure<TaskTypeDto>(TaskErrors.Type.Inactive);
            }

            try
            {
                if (type.AttachForm(request.FormDefinitionId, TaskWrites.Actor(user), timeProvider.GetUtcNow().UtcDateTime))
                {
                    await db.SaveChangesAsync(ct);
                }

                return Result.Success((await TaskTypeProjection.ToDtosAsync(forms, [type], ct))[0]);
            }
            catch (DomainException ex)
            {
                return Result.Failure<TaskTypeDto>(TaskErrors.Type.Invalid(ex.Message));
            }
            catch (DbUpdateException) when (attempt < Attempts)
            {
                // Another writer got there first (a changed row, or the same form filed twice). Start again from what is stored.
                (db as DbContext)?.ChangeTracker.Clear();
            }
            catch (DbUpdateException)
            {
                return Result.Failure<TaskTypeDto>(TaskErrors.Task.ConcurrencyConflict);
            }
        }
    }
}
