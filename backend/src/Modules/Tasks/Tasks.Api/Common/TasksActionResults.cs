using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NWFM.Shared.Results;
using Tasks.Application.Constants;

namespace Tasks.Api.Common;

/// <summary>
/// Turns a <see cref="Result"/> into an HTTP response, the way FormEngine does: a success returns the
/// whole envelope; a failure is written explicitly — reading <c>Result&lt;T&gt;.Value</c> on a failure
/// throws, which a serializer would do mid-response and turn a clean 400 into a 500. The status comes
/// from the error code, including the form engine's codes a fill passes through.
/// </summary>
public static class TasksActionResults
{
    public static IActionResult ToActionResult<T>(this Result<T> result) =>
        result.IsSuccess ? new OkObjectResult(result) : Failure(result.Error);

    public static IActionResult ToActionResult(this Result result) =>
        result.IsSuccess ? new OkObjectResult(result) : Failure(result.Error);

    public static IActionResult ToCreatedResult<T>(this Result<T> result) =>
        result.IsSuccess
            ? new ObjectResult(result) { StatusCode = StatusCodes.Status201Created }
            : Failure(result.Error);

    public static IActionResult Failure(Error error) =>
        new ObjectResult(new
        {
            isSuccess = false,
            isFailure = true,
            error = new { code = error.Code, message = error.Message },
        })
        {
            StatusCode = StatusFor(error.Code),
        };

    private static int StatusFor(string code)
    {
        if (code.EndsWith(TaskErrors.NotFoundSuffix, StringComparison.Ordinal))
        {
            return StatusCodes.Status404NotFound;
        }

        if (TaskErrors.Conflicts.Contains(code))
        {
            return StatusCodes.Status409Conflict;
        }

        return TaskErrors.Forbidden.Contains(code)
            ? StatusCodes.Status403Forbidden
            : StatusCodes.Status400BadRequest;
    }
}
