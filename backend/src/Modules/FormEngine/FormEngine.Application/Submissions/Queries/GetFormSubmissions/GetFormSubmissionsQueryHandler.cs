using FormEngine.Application.Common;
using FormEngine.Application.Common.Interfaces;
using FormEngine.Application.Constants;
using FormEngine.Application.Submissions.Common;
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

        var table = await FormTableLoader.LoadAsync(context, request.FormDefinitionId, ct);

        // Never published, so nothing can have been submitted: an empty page, not an error.
        if (table is null)
        {
            return Result.Success(new PaginatedResult<IReadOnlyDictionary<string, object?>>(
                [], 0, request.PageNumber, request.PageSize));
        }

        var (items, total) = await submissionStore.ListAsync(
            table,
            new FormSubmissionListFilter
            {
                PageNumber = request.PageNumber,
                PageSize = request.PageSize,
                ContextType = request.ContextType,
                ContextId = request.ContextId,
            },
            ct);

        return Result.Success(new PaginatedResult<IReadOnlyDictionary<string, object?>>(
            items.Select(row => SubmissionRows.WithForm(row, request.FormDefinitionId)).ToList(),
            total,
            request.PageNumber,
            request.PageSize));
    }
}
