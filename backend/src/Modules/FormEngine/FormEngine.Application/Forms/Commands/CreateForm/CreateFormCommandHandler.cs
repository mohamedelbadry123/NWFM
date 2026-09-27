using FormEngine.Application.Common.Interfaces;
using FormEngine.Application.Constants;
using FormEngine.Application.Forms.Common;
using FormEngine.Application.Forms.Models;
using FormEngine.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;
using NWFM.Shared.Abstractions;
using NWFM.Shared.Exceptions;
using NWFM.Shared.Results;

namespace FormEngine.Application.Forms.Commands.CreateForm;

public sealed class CreateFormCommandHandler(
    IFormEngineDbContext context,
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

        try
        {
            var form = FormDefinition.Create(
                code,
                request.NameEn,
                request.NameAr,
                request.Category,
                request.DepartmentCode,
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
