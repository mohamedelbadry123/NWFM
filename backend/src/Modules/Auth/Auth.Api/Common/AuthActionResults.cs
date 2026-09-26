using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NWFM.Shared.Results;

namespace Auth.Api.Common;

/// <summary>
/// Turns a <see cref="Result"/> into an HTTP response for the newer Auth endpoints.
/// </summary>
/// <remarks>
/// A successful result is returned whole. A failed one is written explicitly rather than by
/// serializing the result: reading <c>Result&lt;T&gt;.Value</c> on a failure throws, which a serializer
/// would do while writing the response — turning a clean 400 into a 500. The status comes from the
/// error code's shape.
/// </remarks>
public static class AuthActionResults
{
    public static IActionResult ToActionResult<T>(this Result<T> result) =>
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
        if (code.EndsWith("NotFound", StringComparison.Ordinal))
            return StatusCodes.Status404NotFound;

        if (code.Contains("Duplicate", StringComparison.Ordinal) || code.EndsWith("AlreadyExists", StringComparison.Ordinal))
            return StatusCodes.Status409Conflict;

        return StatusCodes.Status400BadRequest;
    }
}
