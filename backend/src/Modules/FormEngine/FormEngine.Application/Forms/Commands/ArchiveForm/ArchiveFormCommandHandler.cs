using FormEngine.Application.Common.Interfaces;
using FormEngine.Application.Constants;
using FormEngine.Application.Forms.Common;
using FormEngine.Application.Forms.Models;
using FormEngine.Domain.Constants;
using MediatR;
using Microsoft.EntityFrameworkCore;
using NWFM.Shared.Abstractions;
using NWFM.Shared.Results;

namespace FormEngine.Application.Forms.Commands.ArchiveForm;

public sealed class ArchiveFormCommandHandler(
    IFormEngineDbContext context,
    ICurrentUser user,
    TimeProvider timeProvider)
    : IRequestHandler<ArchiveFormCommand, Result<FormDetailDto>>
{
    private const string ArchivedAction = "archived";

    public async Task<Result<FormDetailDto>> Handle(ArchiveFormCommand request, CancellationToken ct)
    {
        var form = await context.FormDefinitions.FirstOrDefaultAsync(x => x.Id == request.Id, ct);

        if (form is null)
        {
            return Result.Failure<FormDetailDto>(FormEngineErrors.Form.NotFound);
        }

        if (form.Status == FormStatuses.Archived)
        {
            return Result.Failure<FormDetailDto>(
                FormEngineErrors.Form.InvalidStatusTransition(form.Status, ArchivedAction));
        }

        form.Archive(user.Id, timeProvider.GetUtcNow().UtcDateTime);

        try
        {
            await context.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure<FormDetailDto>(FormEngineErrors.Form.ConcurrencyConflict);
        }

        return Result.Success(form.ToDetailDto());
    }
}
