using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NWFM.Shared.Constants;
using NWFM.Shared.Results;
using Workflow.Application.Mapping;

namespace Workflow.Api.Controllers;

[ApiController]
[Authorize(Policy = NwfmPolicies.ManageDefinitions)]
[Route("api/workflow/definitions/{definitionId:guid}/versions/{versionId:guid}/mapping")]
[RequestSizeLimit(1048576)]
public sealed class WorkflowMappingController(IDesignerMappingCatalog catalog) : ControllerBase
{
    [HttpGet("sources")]
    [ProducesResponseType(typeof(MappingCatalog), 200)]
    public async Task<IActionResult> Sources(Guid definitionId, Guid versionId, CancellationToken ct)
    {
        var result = await catalog.GetAsync(definitionId, versionId, ct);
        return result.IsSuccess ? Ok(result.Value) : NotFound(result.Error);
    }
    [HttpGet("related-definitions")]
    public Task<IReadOnlyList<MappingDefinitionChoice>> Related(CancellationToken ct) => catalog.RelatedAsync(ct);
    [HttpPost("validate")]
    [ProducesResponseType(typeof(MappingValidationResponse), 200)]
    public async Task<IActionResult> Validate(Guid definitionId, Guid versionId, MappingAuthoringRequest input, CancellationToken ct)
    {
        var result = await Context(definitionId, versionId, input.Configuration, ct);
        if (result.IsFailure) return BadRequest(result.Error);
        var errors = MappingValidation.Validate(input.Configuration, result.Value, input.Owner);
        return Ok(new MappingValidationResponse(errors.Count == 0, errors, errors.Count == 0));
    }
    public sealed record MappingValidationResponse(
        [property: System.Text.Json.Serialization.JsonPropertyName("valid")] bool Valid,
        [property: System.Text.Json.Serialization.JsonPropertyName("errors")] IReadOnlyList<string> Errors,
        [property: System.Text.Json.Serialization.JsonPropertyName("canPublish")] bool CanPublish);
    [HttpPost("preview")]
    [ProducesResponseType(typeof(MappingPreviewResult), 200)]
    public async Task<IActionResult> Preview(Guid definitionId, Guid versionId, MappingPreviewRequest input, CancellationToken ct)
    {
        if (input.Owner is null || input.Samples is null) return BadRequest(new { code = "Mapping.Input", message = "Owner and sample list are required." });
        var result = await Context(definitionId, versionId, input.Configuration, ct);
        if (result.IsFailure) return BadRequest(result.Error);
        return Ok(MappingRequestPreparation.Preview(input, result.Value, input.Owner));
    }
    [HttpPost("import-openapi")]
    [ProducesResponseType(typeof(EndpointDefinition), 200)]
    [RequestSizeLimit(524288)]
    public IActionResult Import(SchemaImportRequest input) => Pure(() => MappingSchemaImport.Import(input));
    [HttpPost("validate-schema")]
    [ProducesResponseType(typeof(EndpointDefinition), 200)]
    public IActionResult Schema(EndpointDefinition input) => Pure(() => { MappingValidation.Endpoint(input); return input; });
    [HttpPost("infer-response")]
    [ProducesResponseType(typeof(MappingSchema), 200)]
    [RequestSizeLimit(262144)]
    public IActionResult Infer([FromBody] JsonElement sample) => Pure(() => MappingTypes.InferResponse(sample));
    private IActionResult Pure<T>(Func<T> operation)
    {
        try { return Ok(operation()); }
        catch (Exception ex) when (ex is FormatException or JsonException or InvalidOperationException or ArgumentException or KeyNotFoundException or NullReferenceException)
        { return BadRequest(new { code = "Mapping.Schema", message = ex is NullReferenceException ? "Required schema member is missing." : ex.Message }); }
    }
    private async Task<Result<MappingCatalog>> Context(Guid definitionId, Guid versionId, DesignerMappingConfiguration config, CancellationToken ct)
    {
        if (config?.TreeRoot is not null && (config.TreeRoot.WorkflowDefinitionId != definitionId || config.TreeRoot.WorkflowVersionId != versionId))
            return Result.Failure<MappingCatalog>(new("Mapping.TreePin", "Use the configuration's pinned authoring tree root."));
        var result = await catalog.GetAsync(definitionId, versionId, ct); if (result.IsFailure) return result;
        if (config?.Selectors is null || config.Selectors.Count > 20 || config.Selectors.Any(s => s is null)) return Result.Failure<MappingCatalog>(new("Mapping.Input", "Provide at most 20 selectors."));
        var choices = config.Selectors.Count == 0 ? [] : await catalog.RelatedAsync(ct);
        var sources = result.Value.Sources.ToList();
        var relatedTrees = new List<RelatedMappingTree>();
        foreach (var pin in config.Selectors.Select(s => (s.WorkflowDefinitionId, s.WorkflowVersionId)).Distinct())
        {
            if (!choices.Any(c => c.DefinitionId == pin.WorkflowDefinitionId && c.VersionId == pin.WorkflowVersionId)) return Result.Failure<MappingCatalog>(new("Mapping.Source", "Related source version is unavailable or unauthorized."));
            var related = await catalog.GetAsync(pin.WorkflowDefinitionId, pin.WorkflowVersionId, ct); if (related.IsFailure) return related;
            sources.AddRange(related.Value.Sources);
            relatedTrees.Add(new(new(pin.WorkflowDefinitionId, pin.WorkflowVersionId), related.Value.TreeNodes ?? []));
        }
        return Result.Success(result.Value with { Sources = sources.DistinctBy(s => s.Reference).ToList(), RelatedTrees = relatedTrees });
    }
}
