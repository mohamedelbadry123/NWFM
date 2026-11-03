using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NWFM.Shared.Results;

namespace NWFM.Shared.Http;

/// <summary>
/// Turns a <see cref="Result"/> into an MVC response.
/// </summary>
/// <remarks>
/// A successful result is returned whole. A failed one is written explicitly rather than by
/// serializing the result: reading <c>Result&lt;T&gt;.Value</c> on a failure throws, which a serializer
/// would do while writing the response, turning a clean 400 into a 500. The status is the error's own
/// when it names one, otherwise it comes from the error code's shape.
/// </remarks>
public static class ResultActionResults
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
        new ObjectResult(FailureBody(error.Code, error.Message)) { StatusCode = error.Status ?? StatusFor(error.Code) };

    /// <summary>The failure envelope every error response shares: <c>error.code</c> and <c>error.message</c>.</summary>
    public static object FailureBody(string code, string message) => new
    {
        isSuccess = false,
        isFailure = true,
        error = new { code, message },
    };

    private static int StatusFor(string code)
    {
        if (code.EndsWith("NotFound", StringComparison.Ordinal))
            return StatusCodes.Status404NotFound;
        if (code.EndsWith("Forbidden", StringComparison.Ordinal))
            return StatusCodes.Status403Forbidden;
        if (code.EndsWith("Unauthorized", StringComparison.Ordinal))
            return StatusCodes.Status401Unauthorized;
        if (code.Contains("Duplicate", StringComparison.Ordinal) || code.EndsWith("AlreadyExists", StringComparison.Ordinal))
            return StatusCodes.Status409Conflict;
        return StatusCodes.Status400BadRequest;
    }
}
