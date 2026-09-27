using FormEngine.Api.Common;
using FormEngine.Api.Contracts;
using FormEngine.Application.Forms.Commands.ArchiveForm;
using FormEngine.Application.Forms.Commands.CloneForm;
using FormEngine.Application.Forms.Commands.CreateForm;
using FormEngine.Application.Forms.Commands.DeprecateForm;
using FormEngine.Application.Forms.Commands.PublishForm;
using FormEngine.Application.Forms.Commands.SaveFormSchema;
using FormEngine.Application.Forms.Commands.UpdateForm;
using FormEngine.Application.Forms.Models;
using FormEngine.Application.Forms.Queries.GetFormById;
using FormEngine.Application.Forms.Queries.GetForms;
using FormEngine.Application.Forms.Queries.GetPublishedForms;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NWFM.Shared.Constants;
using NWFM.Shared.Results;

namespace FormEngine.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/form-engine/forms")]
public sealed class FormsController(ISender sender) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = NwfmPolicies.ViewForms)]
    [ProducesResponseType(typeof(Result<PaginatedResult<FormListItemDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetForms([FromQuery] GetFormsQuery query, CancellationToken ct) =>
        (await sender.Send(query, ct)).ToActionResult();

    /// <summary>
    /// The forms that can be filled, with every version a consumer could pin. Open to anyone who may
    /// fill a form, not only to whoever manages them.
    /// </summary>
    [HttpGet("published")]
    [Authorize(Policy = NwfmPolicies.FormPickers)]
    [ProducesResponseType(typeof(Result<IReadOnlyList<PublishedFormDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPublishedForms([FromQuery] GetPublishedFormsQuery query, CancellationToken ct) =>
        (await sender.Send(query, ct)).ToActionResult();

    [HttpGet("{id:guid}")]
    [Authorize(Policy = NwfmPolicies.ViewForms)]
    [ProducesResponseType(typeof(Result<FormDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetForm(Guid id, CancellationToken ct) =>
        (await sender.Send(new GetFormByIdQuery(id), ct)).ToActionResult();

    [HttpPost]
    [Authorize(Policy = NwfmPolicies.ManageForms)]
    [ProducesResponseType(typeof(Result<FormDetailDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateForm([FromBody] CreateFormRequest request, CancellationToken ct)
    {
        var command = new CreateFormCommand
        {
            Code = request.Code,
            NameEn = request.NameEn,
            NameAr = request.NameAr,
            Category = request.Category,
            DepartmentCode = request.DepartmentCode,
        };

        return (await sender.Send(command, ct)).ToCreatedResult();
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = NwfmPolicies.ManageForms)]
    [ProducesResponseType(typeof(Result<FormDetailDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateForm(Guid id, [FromBody] UpdateFormRequest request, CancellationToken ct)
    {
        var command = new UpdateFormCommand
        {
            Id = id,
            NameEn = request.NameEn,
            NameAr = request.NameAr,
            Category = request.Category,
            DepartmentCode = request.DepartmentCode,
        };

        return (await sender.Send(command, ct)).ToActionResult();
    }

    /// <summary>Saves the builder document as the working draft. Nothing is published by this.</summary>
    [HttpPut("{id:guid}/schema")]
    [Authorize(Policy = NwfmPolicies.ManageForms)]
    [ProducesResponseType(typeof(Result<FormDetailDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> SaveSchema(Guid id, [FromBody] SaveFormSchemaRequest request, CancellationToken ct)
    {
        var command = new SaveFormSchemaCommand { Id = id, SchemaJson = request.SchemaJson };

        return (await sender.Send(command, ct)).ToActionResult();
    }

    [HttpPost("{id:guid}/publish")]
    [Authorize(Policy = NwfmPolicies.ManageForms)]
    [ProducesResponseType(typeof(Result<FormDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Publish(Guid id, CancellationToken ct) =>
        (await sender.Send(new PublishFormCommand(id), ct)).ToActionResult();

    [HttpPost("{id:guid}/clone")]
    [Authorize(Policy = NwfmPolicies.ManageForms)]
    [ProducesResponseType(typeof(Result<FormDetailDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Clone(Guid id, [FromBody] CloneFormRequest request, CancellationToken ct)
    {
        var command = new CloneFormCommand
        {
            Id = id,
            NewCode = request.NewCode,
            NewNameEn = request.NewNameEn,
            NewNameAr = request.NewNameAr,
        };

        return (await sender.Send(command, ct)).ToCreatedResult();
    }

    [HttpPost("{id:guid}/deprecate")]
    [Authorize(Policy = NwfmPolicies.ManageForms)]
    [ProducesResponseType(typeof(Result<FormDetailDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Deprecate(Guid id, CancellationToken ct) =>
        (await sender.Send(new DeprecateFormCommand(id), ct)).ToActionResult();

    [HttpPost("{id:guid}/archive")]
    [Authorize(Policy = NwfmPolicies.ManageForms)]
    [ProducesResponseType(typeof(Result<FormDetailDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Archive(Guid id, CancellationToken ct) =>
        (await sender.Send(new ArchiveFormCommand(id), ct)).ToActionResult();
}
