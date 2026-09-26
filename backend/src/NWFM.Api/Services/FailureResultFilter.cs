using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using NWFM.Shared.Results;

namespace NWFM.Api.Services;

/// <summary>Failure results have no Value; serializing that throwing getter must not turn a 400/401 into a 500.</summary>
public sealed class FailureResultFilter : IResultFilter
{
    public void OnResultExecuting(ResultExecutingContext context)
    {
        if (context.Result is ObjectResult { Value: Result { IsFailure: true } failure } result)
            result.Value = new { isSuccess = false, isFailure = true, error = failure.Error };
    }
    public void OnResultExecuted(ResultExecutedContext context) { }
}
