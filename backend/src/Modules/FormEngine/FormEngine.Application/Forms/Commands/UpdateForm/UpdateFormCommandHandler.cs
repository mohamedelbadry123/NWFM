using FormEngine.Application.Common.Interfaces;
using FormEngine.Application.Constants;
using FormEngine.Application.Forms.Common;
using FormEngine.Application.Forms.Models;
using FormEngine.Domain.Constants;
using MediatR;
using Microsoft.EntityFrameworkCore;
using NWFM.Shared.Abstractions;
using NWFM.Shared.Exceptions;
using NWFM.Shared.Integration.Organization;
using NWFM.Shared.Results;

namespace FormEngine.Application.Forms.Commands.UpdateForm;

public sealed class UpdateFormCommandHandler(
    IFormEngineDbContext context,
    IOrgDirectory directory,
    ICurrentUser user,
    TimeProvider timeProvider)
    : IRequestHandler<UpdateFormCommand, Result<FormDetailDto>>
{
    public async Task<Result<FormDetailDto>> Handle(UpdateFormCommand request, CancellationToken ct)
    {
        var form = await context.FormDefinitions.FirstOrDefaultAsync(x => x.Id == request.Id, ct);

        if (form is null)
        {
            return Result.Failure<FormDetailDto>(FormEngineErrors.Form.NotFound);
        }

        if (FormStatuses.IsFrozen(form.Status))
        {
            return Result.Failure<FormDetailDto>(FormEngineErrors.Form.NotEditable(form.Status));
        }

        var department = request.DepartmentCode!.Trim();
        var activity = request.FieldActivityCode!.Trim();

        // Checked only when it changes: an activity later retired must not stop the form being renamed.
        var changed = !string.Equals(form.DepartmentCode, department, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(form.FieldActivityCode, activity, StringComparison.OrdinalIgnoreCase);

        if (changed && !await directory.IsFieldActivityInDepartmentAsync(department, activity, ct))
        {
            return Result.Failure<FormDetailDto>(FormEngineErrors.Form.InvalidFieldActivity(department, activity));
        }

        try
        {
            form.UpdateDetails(
                request.NameEn,
                request.NameAr,
                request.Category,
                department,
                activity,
                user.Id,
                timeProvider.GetUtcNow().UtcDateTime);

            await context.SaveChangesAsync(ct);

            return Result.Success(form.ToDetailDto());
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure<FormDetailDto>(FormEngineErrors.Form.ConcurrencyConflict);
        }
        catch (DomainException ex)
        {
            return Result.Failure<FormDetailDto>(FormEngineErrors.Form.Invalid(ex.Message));
        }
    }
}
