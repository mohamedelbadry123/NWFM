using FormEngine.Application.Common.Interfaces;
using FormEngine.Application.Constants;
using FormEngine.Application.Forms.Common;
using FormEngine.Application.Forms.Models;
using FormEngine.Domain.Constants;
using FormEngine.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;
using NWFM.Shared.Abstractions;
using NWFM.Shared.Exceptions;
using NWFM.Shared.Integration.Organization;
using NWFM.Shared.Results;

namespace FormEngine.Application.Forms.Commands.CreateForm;

public sealed class CreateFormCommandHandler(
    IFormEngineDbContext context,
    IOrgDirectory directory,
    ICurrentUser user,
    TimeProvider timeProvider)
    : IRequestHandler<CreateFormCommand, Result<FormDetailDto>>
{
    public async Task<Result<FormDetailDto>> Handle(CreateFormCommand request, CancellationToken ct)
    {
        var code = request.Code.Trim();

        if (await context.FormDefinitions.AnyAsync(x => x.Code == code, ct))
        {
            return Result.Failure<FormDetailDto>(FormEngineErrors.Form.DuplicateCode(code));
        }

        var department = request.DepartmentCode?.Trim();
        var activity = request.FieldActivityCode?.Trim();

        // Classification is optional at creation; an activity is checked only when one is given.
        if (!string.IsNullOrEmpty(department) && !string.IsNullOrEmpty(activity)
            && !await directory.IsValidFieldActivityAsync(department, activity, ct))
        {
            return Result.Failure<FormDetailDto>(FormEngineErrors.Form.InvalidFieldActivity(department, activity));
        }

        try
        {
            var form = FormDefinition.Create(
                code,
                request.NameEn,
                request.NameAr,
                string.IsNullOrWhiteSpace(request.Category) ? FormCategories.General : request.Category,
                department,
                activity,
                user.Id,
                timeProvider.GetUtcNow().UtcDateTime);

            context.FormDefinitions.Add(form);
            await context.SaveChangesAsync(ct);

            return Result.Success(form.ToDetailDto());
        }
        catch (DomainException ex)
        {
            return Result.Failure<FormDetailDto>(FormEngineErrors.Form.Invalid(ex.Message));
        }
    }
}
