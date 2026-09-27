using FormEngine.Application.Common.Interfaces;
using FormEngine.Application.Constants;
using FormEngine.Application.Forms.Common;
using FormEngine.Application.Forms.Models;
using MediatR;
using Microsoft.EntityFrameworkCore;
using NWFM.Shared.Abstractions;
using NWFM.Shared.Exceptions;
using NWFM.Shared.Results;

namespace FormEngine.Application.Forms.Commands.CloneForm;

public sealed class CloneFormCommandHandler(
    IFormEngineDbContext context,
    ICurrentUser user,
    TimeProvider timeProvider)
    : IRequestHandler<CloneFormCommand, Result<FormDetailDto>>
{
    public async Task<Result<FormDetailDto>> Handle(CloneFormCommand request, CancellationToken ct)
    {
        var source = await context.FormDefinitions
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == request.Id, ct);

        if (source is null)
        {
            return Result.Failure<FormDetailDto>(FormEngineErrors.Form.NotFound);
        }

        var newCode = request.NewCode.Trim();

        if (await context.FormDefinitions.AnyAsync(x => x.Code == newCode, ct))
        {
            return Result.Failure<FormDetailDto>(FormEngineErrors.Form.DuplicateCode(newCode));
        }

        try
        {
            var clone = source.Clone(
                newCode,
                request.NewNameEn,
                request.NewNameAr,
                user.Id,
                timeProvider.GetUtcNow().UtcDateTime);

            context.FormDefinitions.Add(clone);
            await context.SaveChangesAsync(ct);

            return Result.Success(clone.ToDetailDto());
        }
        catch (DomainException ex)
        {
            return Result.Failure<FormDetailDto>(FormEngineErrors.Form.Invalid(ex.Message));
        }
    }
}
