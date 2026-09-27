using FormEngine.Api.Common;
using FormEngine.Api.Contracts;
using FormEngine.Application.Constants;
using FormEngine.Application.Uploads.Commands.DeleteFormFile;
using FormEngine.Application.Uploads.Commands.UploadFormFile;
using FormEngine.Application.Uploads.Models;
using FormEngine.Application.Uploads.Queries.GetFormFile;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NWFM.Shared.Constants;
using NWFM.Shared.Results;

namespace FormEngine.Api.Controllers;

/// <summary>
/// Media for form fields. A file is uploaded the moment it is picked and stays pending until the
/// form is submitted, so bytes never travel inside the submit payload.
/// </summary>
[ApiController]
[Authorize]
[Route("api/v1/form-engine/uploads")]
public sealed class FormUploadsController(ISender sender) : ControllerBase
{
    [HttpPost]
    [Authorize(Policy = NwfmPolicies.FormUploaders)]
    [ProducesResponseType(typeof(Result<UploadedFileDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Upload([FromForm] UploadFormFileRequest request, CancellationToken ct)
    {
        if (request.File is null || request.File.Length == 0)
        {
            return FormEngineActionResults.Failure(FormEngineErrors.File.Empty);
        }

        // Opened, not buffered: the stream is copied straight into storage.
        await using var content = request.File.OpenReadStream();

        var command = new UploadFormFileCommand
        {
            FormDefinitionId = request.FormDefinitionId,
            VersionNo = request.VersionNo,
            DataName = request.DataName,
            FileName = request.File.FileName,
            ContentType = request.File.ContentType,
            SizeBytes = request.File.Length,
            ContextType = request.ContextType,
            ContextId = request.ContextId,
            Content = content,
        };

        return (await sender.Send(command, ct)).ToActionResult();
    }

    [HttpDelete("{fileId:guid}")]
    [Authorize(Policy = NwfmPolicies.FormUploaders)]
    [ProducesResponseType(typeof(Result), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(Guid fileId, CancellationToken ct) =>
        (await sender.Send(new DeleteFormFileCommand(fileId), ct)).ToActionResult();

    /// <summary>Streams the stored file. The response owns the stream and disposes it.</summary>
    [HttpGet("{fileId:guid}")]
    [Authorize(Policy = NwfmPolicies.SubmitOrViewSubmissions)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Download(Guid fileId, CancellationToken ct)
    {
        var result = await sender.Send(new GetFormFileQuery(fileId), ct);

        if (result.IsFailure)
        {
            return FormEngineActionResults.Failure(result.Error);
        }

        var file = result.Value;

        return File(file.Content, file.ContentType, file.FileName);
    }
}
