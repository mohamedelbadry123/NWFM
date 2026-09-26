using FormEngine.Application.Common.Interfaces;
using FormEngine.Application.Common.Schema;
using FormEngine.Application.Constants;
using FormEngine.Application.Forms.Common;
using FormEngine.Application.Forms.Models;
using FormEngine.Domain.Constants;
using MediatR;
using Microsoft.EntityFrameworkCore;
using NWFM.Shared.Abstractions;
using NWFM.Shared.Exceptions;
using NWFM.Shared.Results;

namespace FormEngine.Application.Forms.Commands.SaveFormSchema;

public sealed class SaveFormSchemaCommandHandler(
    IFormEngineDbContext context,
    ICurrentUser user,
    TimeProvider timeProvider)
    : IRequestHandler<SaveFormSchemaCommand, Result<FormDetailDto>>
{
    public async Task<Result<FormDetailDto>> Handle(SaveFormSchemaCommand request, CancellationToken ct)
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

        try
        {
            // The builder carries the form's names in the document too; keeping them in step means
            // the grid and the designer never disagree about what a form is called.
            var schema = FormSchemaParser.Parse(request.SchemaJson);

            form.SetSchema(
                request.SchemaJson,
                schema.NameEn,
                schema.NameAr,
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
