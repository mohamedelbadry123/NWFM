using FormEngine.Application.Common.Interfaces;
using FormEngine.Application.Constants;
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

        var row = await submissionStore.GetByIdAsync(request.FormDefinitionId, request.SubmissionId, ct);

        return row is null
            ? Result.Failure<IReadOnlyDictionary<string, object?>>(FormEngineErrors.Submission.NotFound)
            : Result.Success(row);
    }
}
