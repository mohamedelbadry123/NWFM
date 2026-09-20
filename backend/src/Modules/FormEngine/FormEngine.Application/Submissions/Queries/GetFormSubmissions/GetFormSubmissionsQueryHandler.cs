using FormEngine.Application.Common.Interfaces;
using FormEngine.Application.Constants;
using MediatR;
using Microsoft.EntityFrameworkCore;
using NWFM.Shared.Results;

namespace FormEngine.Application.Submissions.Queries.GetFormSubmissions;

public sealed class GetFormSubmissionsQueryHandler(
    IFormEngineDbContext context,
    IFormSubmissionStore submissionStore)
    : IRequestHandler<GetFormSubmissionsQuery, Result<PaginatedResult<IReadOnlyDictionary<string, object?>>>>
{
    public async Task<Result<PaginatedResult<IReadOnlyDictionary<string, object?>>>> Handle(
        GetFormSubmissionsQuery request,
        CancellationToken ct)
    {
        if (!await context.FormDefinitions.AnyAsync(x => x.Id == request.FormDefinitionId, ct))
        {
            return Result.Failure<PaginatedResult<IReadOnlyDictionary<string, object?>>>(FormEngineErrors.Form.NotFound);
        }

        var (items, total) = await submissionStore.ListAsync(
            new FormSubmissionListFilter
            {
                FormDefinitionId = request.FormDefinitionId,
                PageNumber = request.PageNumber,
                PageSize = request.PageSize,
                ContextType = request.ContextType,
                ContextId = request.ContextId,
            },
            ct);

        return Result.Success(
            new PaginatedResult<IReadOnlyDictionary<string, object?>>(items, total, request.PageNumber, request.PageSize));
    }
}
