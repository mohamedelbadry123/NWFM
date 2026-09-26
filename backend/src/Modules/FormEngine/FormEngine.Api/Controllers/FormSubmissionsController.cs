using FormEngine.Api.Common;
using FormEngine.Api.Contracts;
using FormEngine.Application.Submissions.Commands.SubmitForm;
using FormEngine.Application.Submissions.Models;
using FormEngine.Application.Submissions.Queries.GetFormSubmissionById;
using FormEngine.Application.Submissions.Queries.GetFormSubmissions;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NWFM.Shared.Constants;
using NWFM.Shared.Results;

namespace FormEngine.Api.Controllers;

/// <summary>
/// Filled-in forms. Every submission names the version it answered, so a row can always be rendered
/// with the schema that produced it.
/// </summary>
[ApiController]
[Authorize]
[Route("api/v1/form-engine/forms/{formId:guid}/submissions")]
public sealed class FormSubmissionsController(ISender sender) : ControllerBase
{
    /// <summary>
    /// Records a fill. Returns 201 for a new submission and 200 when the client key shows this is a
    /// retry of one already recorded.
    /// </summary>
    [HttpPost]
    [Authorize(Policy = NwfmPolicies.SubmitForms)]
    [ProducesResponseType(typeof(Result<FormSubmissionCreatedDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(Result<FormSubmissionCreatedDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Submit(Guid formId, [FromBody] SubmitFormRequest request, CancellationToken ct)
    {
        var command = new SubmitFormCommand
        {
            FormDefinitionId = formId,
            VersionNo = request.VersionNo,
            ContextType = request.ContextType,
            ContextId = request.ContextId,
            ClientSubmissionId = request.ClientSubmissionId,
            ClientFilledAt = request.ClientFilledAt,
            Answers = request.Answers,
        };

        var result = await sender.Send(command, ct);

        if (result.IsFailure)
        {
            return FormEngineActionResults.Failure(result.Error);
        }

        return result.Value.IsReplay ? result.ToActionResult() : result.ToCreatedResult();
    }

    [HttpGet]
    [Authorize(Policy = NwfmPolicies.ViewSubmissions)]
    [ProducesResponseType(typeof(Result<PaginatedResult<IReadOnlyDictionary<string, object?>>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetSubmissions(
        Guid formId,
        [FromQuery] GetFormSubmissionsQuery query,
        CancellationToken ct) =>
        (await sender.Send(query with { FormDefinitionId = formId }, ct)).ToActionResult();

    [HttpGet("{submissionId:guid}")]
    [Authorize(Policy = NwfmPolicies.ViewSubmissions)]
    [ProducesResponseType(typeof(Result<IReadOnlyDictionary<string, object?>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetSubmission(Guid formId, Guid submissionId, CancellationToken ct) =>
        (await sender.Send(new GetFormSubmissionByIdQuery(formId, submissionId), ct)).ToActionResult();
}
