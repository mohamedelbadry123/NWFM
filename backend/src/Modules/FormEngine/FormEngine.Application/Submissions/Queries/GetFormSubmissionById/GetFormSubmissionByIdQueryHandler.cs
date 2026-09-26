using FormEngine.Application.Common;
using FormEngine.Application.Common.Interfaces;
using FormEngine.Application.Constants;
using FormEngine.Application.Submissions.Common;
using MediatR;
using Microsoft.EntityFrameworkCore;
using NWFM.Shared.Results;

namespace FormEngine.Application.Submissions.Queries.GetFormSubmissionById;

public sealed class GetFormSubmissionByIdQueryHandler(
    IFormEngineDbContext context,
    IFormSubmissionStore submissionStore)
    : IRequestHandler<GetFormSubmissionByIdQuery, Result<IReadOnlyDictionary<string, object?>>>
{
    public async Task<Result<IReadOnlyDictionary<string, object?>>> Handle(
        GetFormSubmissionByIdQuery request,
        CancellationToken ct)
    {
        if (!await context.FormDefinitions.AnyAsync(x => x.Id == request.FormDefinitionId, ct))
        {
            return Result.Failure<IReadOnlyDictionary<string, object?>>(FormEngineErrors.Form.NotFound);
        }

        var table = await FormTableLoader.LoadAsync(context, request.FormDefinitionId, ct);

        var row = table is null
            ? null
            : await submissionStore.GetByIdAsync(table, request.SubmissionId, ct);

        return row is null
            ? Result.Failure<IReadOnlyDictionary<string, object?>>(FormEngineErrors.Submission.NotFound)
            : Result.Success(SubmissionRows.WithForm(row, request.FormDefinitionId));
    }
}
