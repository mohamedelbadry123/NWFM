using System.Text;
using System.Text.Json;

namespace Workflow.Application.Mapping;

public static class MappingRequestPreparation
{
    public static void ValidatePrepared(PreparedMappingRequest request)
    {
        if (request.Method is not ("GET" or "POST" or "PUT" or "PATCH" or "DELETE" or "HEAD" or "OPTIONS")) throw new FormatException("Unsupported request method.");
        MappingValidation.Path(request.Path, false);
        if (request.Query is null || request.Headers is null || request.Query.Count > 100 || request.Headers.Count > 100) throw new FormatException("Invalid request parameters.");
        foreach (var pair in request.Headers) MappingValidation.Header(pair.Key, pair.Value);
        if (request.Query.Any(p => string.IsNullOrEmpty(p.Key) || p.Value is null || p.Key.Any(char.IsControl))) throw new FormatException("Invalid query parameter.");
        if (Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(request)) > MappingTypes.MaxBytes) throw new FormatException("Prepared request exceeds 256 KB.");
    }
    public static void ValidatePreparedForEndpoint(PreparedMappingRequest request, EndpointDefinition endpoint)
    {
        ValidatePrepared(request); MappingValidation.Endpoint(endpoint);
        if (request.Method != endpoint.Method) throw new FormatException("Prepared method differs from the selected operation.");
        var actual = request.Path.Split('/'); var template = endpoint.Path.Split('/');
        if (actual.Length != template.Length) throw new FormatException("Prepared path differs from the selected operation.");
        var pathValues = new Dictionary<string, string>();
        for (var i = 0; i < actual.Length; i++)
        {
            if (template[i].StartsWith('{') && template[i].EndsWith('}')) pathValues.Add(template[i][1..^1], Uri.UnescapeDataString(actual[i]));
            else if (actual[i] != template[i]) throw new FormatException("Prepared path differs from the selected operation.");
        }
        foreach (var pair in request.Query) if (!endpoint.Parameters.Any(p => p.Location == "query" && p.Name == pair.Key)) throw new FormatException("Undeclared query parameter.");
        foreach (var pair in request.Headers) if (!endpoint.Parameters.Any(p => p.Location == "header" && p.Name.Equals(pair.Key, StringComparison.OrdinalIgnoreCase))) throw new FormatException("Undeclared header.");
        foreach (var parameter in endpoint.Parameters)
        {
            var values = parameter.Location == "path" ? pathValues : parameter.Location == "query" ? request.Query : request.Headers;
            JsonElement value = default;
            if (values.TryGetValue(parameter.Name, out var text))
                value = parameter.Schema.Type == "string" ? JsonSerializer.SerializeToElement(text) : JsonSerializer.Deserialize<JsonElement>(text);
            MappingTypes.ValidateValue(parameter.Schema, value);
        }
        if (endpoint.Body is null) { if (request.Body.ValueKind != JsonValueKind.Undefined) throw new FormatException("This operation has no request body."); }
        else MappingTypes.ValidateValue(endpoint.Body, request.Body);
    }
    public static MappingPreviewResult Preview(MappingPreviewRequest input, MappingCatalog catalog, CallableTarget owner)
    {
        var errors = MappingValidation.Validate(input.Configuration, catalog, owner);
        if (errors.Count != 0) return new(null, errors);
        try
        {
            if (input.Samples is null || input.Samples.Count > 500 || input.Samples.Any(s => s is null || s.Source is null)
                || input.Samples.Select(s => s.Source).Distinct().Count() != input.Samples.Count) throw new FormatException("Provide at most 500 unique whole-field samples.");
            foreach (var sample in input.Samples)
            {
                var reference = sample.Source;
                var descriptor = catalog.Sources.SingleOrDefault(s => s.Reference == reference with { SelectorId = null });
                var local = reference.WorkflowDefinitionId == owner.WorkflowDefinitionId && reference.WorkflowVersionId == owner.WorkflowVersionId && reference.NodeKey == owner.NodeKey
                    && reference.FormId is null && reference.FormVersion is null && reference.FormStepId is null && reference.SelectorId is null;
                var schema = local && reference.Kind == "Response" && reference.FieldKey == "$response" ? input.Configuration.Endpoint?.Response
                    : local && reference.Kind == "InvocationInput" && reference.FieldKey == "$inputs" && input.Configuration.Callable ? input.Configuration.InvocationInputs : descriptor?.Schema;
                if (reference.JsonPointer != "" || schema is null) throw new FormatException("Samples require an authorized whole field source pin.");
                MappingTypes.ValidateValue(schema, sample.Value);
            }
            return input.Stage switch
            {
                "Request" => new(Prepare(input.Configuration, input.Samples), []),
                "Completion" => Complete(input.Configuration, input.Samples, catalog, owner),
                "Step" => new(null, [], StepValues: input.Configuration.StepBindings.ToDictionary(b => b.Id, b => EvaluateTyped(b.Value, b.Schema, input.Samples))),
                _ => new(null, ["Select Request, Completion or Step preview."])
            };
        }
        catch (Exception ex) when (ex is FormatException or InvalidOperationException or ArgumentException or JsonException or NullReferenceException) { return new(null, [ex is NullReferenceException ? "Invalid sample structure." : ex.Message]); }
    }
    public static JsonElement EvaluateTyped(TransformNode value, MappingSchema schema, IReadOnlyList<MappingSample> samples, Action<SourceReference>? observed = null)
    {
        var result = MappingTypes.ApplyDefaults(schema, MappingEvaluator.Evaluate(value, samples, observed));
        MappingTypes.ValidateValue(schema with { Required = true }, result); return result;
    }
    /// <summary>Pure completion calculation. The caller must atomically persist outputs and schedule returned actions exactly once.</summary>
    public static MappingPreviewResult Complete(DesignerMappingConfiguration config, IReadOnlyList<MappingSample> samples, MappingCatalog catalog, CallableTarget owner,
        Action<string, string, IReadOnlyList<SourceReference>>? provenance = null)
    {
        var values = samples.Where(s => !(s.Source.WorkflowVersionId == owner.WorkflowVersionId && s.Source.NodeKey == owner.NodeKey && s.Source.Kind == "EventOutput")).ToList();
        var outputs = new Dictionary<string, JsonElement>();
        foreach (var output in config.Outputs)
        {
            var used = new HashSet<SourceReference>();
            var value = EvaluateTyped(output.Value, output.Schema, values, s => used.Add(s)); outputs.Add(output.Id, value);
            provenance?.Invoke("Output", output.Id, used.ToList());
            values.Add(new(new(owner.WorkflowDefinitionId, owner.WorkflowVersionId, owner.NodeKey, "EventOutput", output.Id), value));
        }
        var actions = config.Actions.Select(a =>
        {
            var used = new HashSet<SourceReference>();
            var inputs = EvaluateTyped(a.Inputs, catalog.Targets.Single(t => t.Target == a.Target && t.Callable).Inputs, values, s => used.Add(s));
            provenance?.Invoke("Action", a.Id, used.ToList());
            return new PreparedFollowUp(a.Id, a.Target, a.Mode, inputs, a.UnavailableTarget);
        }).ToList();
        return new(null, [], outputs, actions);
    }
    /// <summary>Requires a validated frozen configuration. This function performs no network or workflow writes.</summary>
    public static PreparedMappingRequest Prepare(DesignerMappingConfiguration config, IReadOnlyList<MappingSample> samples, Action<SourceReference>? observed = null)
    {
        if (config.Endpoint is null) throw new FormatException("Step-only mappings have no request. Select Step preview.");
        MappingValidation.Endpoint(config.Endpoint);
        JsonElement Value(string location, string name, MappingSchema schema)
        {
            var bindings = config.Inputs.Where(b => b.Location == location && b.Name == name).ToList();
            if (bindings.Count > 1) throw new FormatException("Duplicate mapping destination.");
            var value = bindings.Count == 1 ? MappingEvaluator.Evaluate(bindings[0].Value, samples, observed) : schema.Default;
            value = MappingTypes.ApplyDefaults(schema, value); MappingTypes.ValidateValue(schema, value); return value;
        }
        var path = config.Endpoint.Path; var query = new Dictionary<string, string>(StringComparer.Ordinal); var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in config.Endpoint.Parameters)
        {
            var value = Value(p.Location, p.Name, p.Schema); if (value.ValueKind == JsonValueKind.Undefined) continue;
            var text = MappingEvaluator.ScalarText(value);
            if (p.Location == "path")
            {
                if (text.Length == 0 || text is "." or ".." || text.IndexOfAny(['/', '\\', '%', '?', '#']) >= 0 || text.Any(char.IsControl)) throw new FormatException("Unsafe path segment.");
                path = path.Replace("{" + p.Name + "}", Uri.EscapeDataString(text), StringComparison.Ordinal);
            }
            else if (p.Location == "query") query.Add(p.Name, text);
            else { MappingValidation.Header(p.Name, text); headers.Add(p.Name, text); }
        }
        MappingValidation.Path(path, false);
        var body = config.Endpoint.Body is null ? default : Value("body", "$body", config.Endpoint.Body);
        var result = new PreparedMappingRequest(config.Endpoint.Method, path, query, headers, body);
        // Undefined represents no body; it cannot be serialized as a standalone JSON value.
        var size = Encoding.UTF8.GetByteCount(path) + query.Sum(p => Encoding.UTF8.GetByteCount(p.Key + p.Value))
            + headers.Sum(p => Encoding.UTF8.GetByteCount(p.Key + p.Value)) + (body.ValueKind == JsonValueKind.Undefined ? 0 : Encoding.UTF8.GetByteCount(body.GetRawText()));
        if (size > MappingTypes.MaxBytes) throw new FormatException("Prepared request exceeds 256 KB.");
        return result;
    }
}
