using FormEngine.Application.Constants;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NWFM.Shared.Results;

namespace FormEngine.Api.Common;

/// <summary>
/// Turns a <see cref="Result"/> into an HTTP response.
/// </summary>
/// <remarks>
/// A successful result is returned whole, as the Auth module does, so clients read one envelope
/// everywhere. A failed one is written explicitly rather than by serializing the result: accessing
/// <c>Result&lt;T&gt;.Value</c> on a failure throws, which a serializer would do while writing the
/// response — turning a clean 400 into a 500. The status code comes from the error code, so a handler
/// never has to know about HTTP.
/// </remarks>
public static class FormEngineActionResults
{
    public static IActionResult ToActionResult<T>(this Result<T> result) =>
        result.IsSuccess
            ? new OkObjectResult(result)
            : Failure(result.Error);

    public static IActionResult ToActionResult(this Result result) =>
        result.IsSuccess
            ? new OkObjectResult(result)
            : Failure(result.Error);

    /// <summary>For a create: 201 with the same envelope, or the failure response.</summary>
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
        if (code.EndsWith(FormEngineErrors.NotFoundSuffix, StringComparison.Ordinal))
        {
            return StatusCodes.Status404NotFound;
        }

        if (FormEngineErrors.Conflicts.Contains(code))
        {
            return StatusCodes.Status409Conflict;
        }

        return FormEngineErrors.Forbidden.Contains(code)
            ? StatusCodes.Status403Forbidden
            : StatusCodes.Status400BadRequest;
    }
}
