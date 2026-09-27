using FormEngine.Application.Common.Interfaces;
using FormEngine.Application.Constants;
using FormEngine.Application.Forms.Common;
using FormEngine.Application.Forms.Models;
using MediatR;
using Microsoft.EntityFrameworkCore;
using NWFM.Shared.Results;

namespace FormEngine.Application.Forms.Queries.GetFormById;

public sealed class GetFormByIdQueryHandler(IFormEngineDbContext context)
    : IRequestHandler<GetFormByIdQuery, Result<FormDetailDto>>
{
    public async Task<Result<FormDetailDto>> Handle(GetFormByIdQuery request, CancellationToken ct)
    {
        var form = await context.FormDefinitions
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == request.Id, ct);

        return form is null
            ? Result.Failure<FormDetailDto>(FormEngineErrors.Form.NotFound)
            : Result.Success(form.ToDetailDto());
    }
}
