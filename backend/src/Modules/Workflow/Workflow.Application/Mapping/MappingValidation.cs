using System.Text.Json;
using System.Text.RegularExpressions;

namespace Workflow.Application.Mapping;

public static class MappingValidation
{
    public static IReadOnlyList<string> Validate(DesignerMappingConfiguration c, MappingCatalog catalog, CallableTarget owner)
    {
        try { Check(c, catalog, owner); return []; }
        catch (Exception ex) when (ex is FormatException or JsonException or InvalidOperationException or ArgumentException or NullReferenceException)
        { return [ex is NullReferenceException ? "Mapping configuration contains a null collection or required member." : ex.Message]; }
    }
    public static void Policy(MissingDataPolicy? policy)
    {
        if (policy is null) return;
        if (policy.Mode is not ("Attention" or "Wait" or "Fallback")) throw new FormatException("Unknown missing-data policy.");
        if (policy.Mode == "Wait" && (policy.DeadlineSeconds is not (>= 1 and <= 31536000) || string.IsNullOrWhiteSpace(policy.TimeoutOutcome))) throw new FormatException("Wait requires a bounded deadline and timeout outcome.");
        if (policy.Mode != "Wait" && (policy.DeadlineSeconds is not null || policy.TimeoutOutcome is not null)) throw new FormatException("Only Wait accepts a deadline/outcome.");
        if (policy.Mode == "Fallback" != (policy.Fallback.ValueKind != JsonValueKind.Undefined)) throw new FormatException("Fallback must be explicit and only belongs to Fallback policy.");
        MappingTypes.Bound(policy.Fallback);
    }
    public static void Endpoint(EndpointDefinition endpoint)
    {
        if (endpoint.Method is not ("GET" or "POST" or "PUT" or "PATCH" or "DELETE" or "HEAD" or "OPTIONS")) throw new FormatException("Unsupported HTTP method.");
        Path(endpoint.Path, true);
        if (endpoint.Parameters.Count > 100) throw new FormatException("Too many endpoint parameters.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in endpoint.Parameters)
        {
            MappingTypes.Name(p.Name); MappingTypes.ValidateSchema(p.Schema);
            if (p.Location is not ("path" or "query" or "header") || !names.Add(p.Location + ":" + p.Name)) throw new FormatException("Duplicate or unsupported parameter destination.");
            if (p.Schema.Type is "object" or "array" || p.Schema.Nullable) throw new FormatException("Parameters must be non-null scalars; use JSON body for structured values.");
            if (p.Location == "header") Header(p.Name, "");
            if (p.Location == "path" && (!Regex.IsMatch(p.Name, "^[A-Za-z][A-Za-z0-9_]*$") || !p.Schema.Required || !endpoint.Path.Contains("{" + p.Name + "}", StringComparison.Ordinal))) throw new FormatException("Path parameters must be required and present in the path.");
        }
        foreach (Match match in Regex.Matches(endpoint.Path, @"\{([^{}]+)\}"))
            if (!endpoint.Parameters.Any(p => p.Location == "path" && p.Name == match.Groups[1].Value)) throw new FormatException("Missing path parameter schema.");
        if (endpoint.Body is not null) MappingTypes.ValidateSchema(endpoint.Body);
        if (endpoint.Response is not null) MappingTypes.ValidateSchema(endpoint.Response);
    }
    public static void Path(string path, bool template)
    {
        if (string.IsNullOrEmpty(path) || path.Length > 4096 || !path.StartsWith('/') || path.StartsWith("//", StringComparison.Ordinal)
            || path.Any(c => char.IsControl(c) || char.IsWhiteSpace(c)) || path.IndexOfAny(['\\', '?', '#']) >= 0)
            throw new FormatException("Use a connection-relative absolute path without query, fragment or control characters.");
        var normalized = template ? Regex.Replace(path, @"\{[A-Za-z][A-Za-z0-9_]*\}", "value") : path;
        if (normalized.IndexOfAny(['{', '}']) >= 0) throw new FormatException("Invalid path placeholder.");
        foreach (var part in normalized.Split('/'))
        {
            var decoded = Uri.UnescapeDataString(part);
            if (decoded is "." or ".." || decoded.IndexOfAny(['/', '\\', '%', '?', '#']) >= 0 || decoded.Any(char.IsControl)) throw new FormatException("Path traversal, separators and double encoding are not allowed in path segments.");
        }
    }
    public static void Header(string name, string value)
    {
        if (!Regex.IsMatch(name, "^[!#$%&'*+.^_`|~0-9A-Za-z-]+$") || value.Any(c => c < 32 || c > 126)
            || new[] { "Authorization", "Proxy-Authorization", "Cookie", "Host", "Content-Length", "Content-Type", "Connection", "Transfer-Encoding", "Trailer", "TE", "Upgrade", "Expect" }.Contains(name, StringComparer.OrdinalIgnoreCase)
            || name.StartsWith("Proxy-", StringComparison.OrdinalIgnoreCase) || name.StartsWith("Sec-", StringComparison.OrdinalIgnoreCase))
            throw new FormatException("Header is not permitted in a mapping: " + name);
    }
    private static void Check(DesignerMappingConfiguration c, MappingCatalog catalog, CallableTarget owner)
    {
        if (c.ContractVersion != 1) throw new FormatException("Unsupported mapping contract version.");
        if (catalog.TreeNodes is not null && !catalog.TreeNodes.Contains(owner)) throw new FormatException("Mapping owner is outside this workflow tree.");
        if (c.Endpoint is not null) Endpoint(c.Endpoint);
        else if (c.Inputs.Count > 0 || c.Outputs.Count > 0 || c.Actions.Count > 0 || c.Callable || c.InvocationInputs is not null)
            throw new FormatException("An endpoint is required for event mappings. Step-only bindings have no endpoint.");
        if (c.Inputs.Count > 100 || c.Outputs.Count > 100 || c.Selectors.Count > 20 || c.Actions.Count > 20 || c.StepBindings.Count > 100) throw new FormatException("Mapping collection limit exceeded.");
        void Unique(IEnumerable<string> ids) { var seen = new HashSet<string>(StringComparer.Ordinal); foreach (var id in ids) { MappingTypes.Name(id); if (!seen.Add(id)) throw new FormatException("Duplicate stable identifier or destination: " + id); } }
        Unique(c.Selectors.Select(s => s.Id)); Unique(c.Outputs.Select(o => o.Id)); Unique(c.Outputs.Select(o => o.Name)); Unique(c.Actions.Select(a => a.Id)); Unique(c.StepBindings.Select(b => b.Id));
        Unique(c.StepBindings.Select(b => b.FormStepId + ":" + b.FieldKey)); Unique(c.Inputs.Select(b => b.Location + ":" + (b.Location == "header" ? b.Name.ToLowerInvariant() : b.Name)));
        var availableOutputs = 0;
        var typeWork = 0;
        MappingSchema Type(TransformNode n, MappingSchema? item = null, bool response = false)
        {
            if (++typeWork > 10000) throw new FormatException("Mapping type-check work limit exceeded.");
            var args = n.Args ?? [];
            MappingSchema Arg(int i) => Type(args[i], item, response);
            void Need(int i, string type) { var a = Arg(i); if (a.Nullable || a.Type != type && !(type == "number" && a.Type == "integer")) throw new FormatException("Transform requires " + type + "; use an explicit cast."); }
            switch (n.Op)
            {
                case "literal":
                    if (n.LiteralSchema is not null) return n.LiteralSchema;
                    if (n.Value.ValueKind == JsonValueKind.Null) return new("null", Nullable: true);
                    return LiteralType(n.Value);
                case "source":
                    var r = n.Source!; MappingSchema schema;
                    if (r.WorkflowDefinitionId == Guid.Empty || r.WorkflowVersionId == Guid.Empty || string.IsNullOrEmpty(r.NodeKey)) throw new FormatException("Source pins are required.");
                    if (r.Kind == "Response")
                    {
                        if (!response || r.WorkflowDefinitionId != owner.WorkflowDefinitionId || r.WorkflowVersionId != owner.WorkflowVersionId || r.NodeKey != owner.NodeKey || r.FieldKey != "$response" || r.SelectorId is not null || r.FormId is not null || r.FormStepId is not null || r.FormVersion is not null)
                            throw new FormatException("Response is available only to this event's outputs.");
                        schema = MappingTypes.SelectSchema(c.Endpoint?.Response ?? throw new FormatException("Declare a response schema."), r.JsonPointer);
                    }
                    else if (r.SelectorId is null && r.WorkflowVersionId == owner.WorkflowVersionId && r.WorkflowDefinitionId == owner.WorkflowDefinitionId && r.NodeKey == owner.NodeKey && r.Kind is "EventOutput" or "InvocationInput")
                    {
                        if (r.FormId is not null || r.FormStepId is not null || r.FormVersion is not null || r.SelectorId is not null) throw new FormatException("Invalid local event source identity.");
                        if (r.Kind == "InvocationInput")
                            schema = c.Callable && r.FieldKey == "$inputs" && c.InvocationInputs is not null ? MappingTypes.SelectSchema(c.InvocationInputs, r.JsonPointer) : throw new FormatException("Declare callable invocation inputs.");
                        else schema = response ? MappingTypes.SelectSchema(c.Outputs.Take(availableOutputs).SingleOrDefault(o => o.Id == r.FieldKey)?.Schema ?? throw new FormatException("Outputs may reference only previously declared outputs; forward references/cycles are invalid."), r.JsonPointer)
                            : throw new FormatException("An event cannot depend on its own future output.");
                    }
                    else
                    {
                        var descriptor = catalog.Sources.SingleOrDefault(s => s.Reference == r with { JsonPointer = "", SelectorId = null }) ?? throw new FormatException("Unknown, unavailable or unauthorized source pin: " + r.FieldKey);
                        if (descriptor.Availability == "SchemaRequired" || descriptor.Schema.Type == "unknown") throw new FormatException("Declare a typed schema before using this source.");
                        schema = MappingTypes.SelectSchema(descriptor.Schema, r.JsonPointer);
                        if (r.SelectorId is not null)
                        {
                            var selector = c.Selectors.SingleOrDefault(s => s.Id == r.SelectorId);
                            if (selector is null || !(selector.WorkflowDefinitionId == r.WorkflowDefinitionId && selector.WorkflowVersionId == r.WorkflowVersionId
                                || catalog.RelatedTrees?.Any(t => t.Root == new MappingTreePin(selector.WorkflowDefinitionId, selector.WorkflowVersionId)
                                    && t.Nodes.Contains(new(r.WorkflowDefinitionId, r.WorkflowVersionId, r.NodeKey))) == true)) throw new FormatException("Related source selector does not match its pinned tree.");
                        }
                        else if (!(catalog.TreeNodes ?? catalog.Targets.Select(t => t.Target).ToList()).Any(t => t.WorkflowDefinitionId == r.WorkflowDefinitionId && t.WorkflowVersionId == r.WorkflowVersionId)) throw new FormatException("Related sources require an explicit instance selector.");
                        if (!response && r.SelectorId is null && r.NodeKey == owner.NodeKey && r.WorkflowVersionId == owner.WorkflowVersionId && r.Kind != "InvocationInput") throw new FormatException("An event cannot depend on its own future output.");
                        if (r.Kind == "InvocationInput" && (r.WorkflowVersionId != owner.WorkflowVersionId || r.NodeKey != owner.NodeKey || !c.Callable)) throw new FormatException("Invocation inputs belong only to the receiving callable event.");
                        if (r.SelectorId is null && r.WorkflowVersionId == owner.WorkflowVersionId && r.Kind is "EventOutput" or "RoutingOutput" or "FormField"
                            && Reachable(owner, new(r.WorkflowDefinitionId, r.WorkflowVersionId, r.NodeKey), "Graph", catalog.Dependencies ?? [])
                            && !Reachable(new(r.WorkflowDefinitionId, r.WorkflowVersionId, r.NodeKey), owner, "Graph", catalog.Dependencies ?? [])) throw new FormatException("Source cannot complete before the receiving node.");
                    }
                    if (n.Missing?.Mode == "Fallback") MappingTypes.ValidateValue(schema with { Required = true }, n.Missing.Fallback);
                    return schema;
                case "item": return MappingTypes.SelectSchema(item ?? throw new FormatException("Item has no array context."), n.JsonPointer);
                case "object": return new("object", Properties: n.Properties!.ToDictionary(p => p.Key, p => Type(p.Value, item, response) with { Required = true }));
                case "concat": for (var i = 0; i < args.Count; i++) Need(i, "string"); return new("string");
                case "add": case "subtract": case "multiply": case "divide": Need(0, "number"); Need(1, "number"); return new("number");
                case "text":
                case "number":
                case "integer":
                case "boolean":
                    var input = Arg(0); if (input.Type is "object" or "array" or "null") throw new FormatException("Cast requires a scalar.");
                    return new(n.Op == "text" ? "string" : n.Op);
                case "coalesce":
                    var types = args.Select(a => Type(a, item, response)).Where(t => t.Type != "null").ToList();
                    if (types.Count == 0) throw new FormatException("Coalesce needs a typed non-null alternative.");
                    var common = types.Any(t => t.Type == "number") && types.All(t => t.Type is "integer" or "number") ? new MappingSchema("number") : types[0];
                    if (types.Any(t => !MappingTypes.Compatible(t with { Nullable = false }, common with { Nullable = false }))) throw new FormatException("Coalesce values need compatible types.");
                    return common with { Nullable = args.All(a => Type(a, item, response).Nullable) };
                case "map":
                case "filter":
                    Need(0, "array"); var mapped = Type(args[1], Arg(0).Items, response);
                    if (n.Op == "filter" && (mapped.Type != "boolean" || mapped.Nullable)) throw new FormatException("Filter requires a boolean predicate.");
                    return n.Op == "map" ? new("array", Items: mapped) : Arg(0);
                case "count": Need(0, "array"); return new("integer");
                case "sum": Need(0, "array"); if (Arg(0).Items is not { Type: "number" or "integer", Nullable: false }) throw new FormatException("Sum requires a numeric array."); return new("number");
                case "greater": Need(0, "number"); Need(1, "number"); return new("boolean");
                case "equal": if (!MappingTypes.Comparable(Arg(0), Arg(1))) throw new FormatException("Equality requires compatible types."); return new("boolean");
                default: throw new FormatException("Unsupported transform.");
            }
        }
        void Expression(TransformNode n, MappingSchema target, bool response = false)
        {
            MappingEvaluator.ValidateTree(n); var type = Type(n, response: response);
            if (type.Type == "null" ? !target.Nullable : !MappingTypes.Compatible(type, target)) throw new FormatException("Mapping type is incompatible with destination " + target.Type + ".");
            if (n.Op == "literal") MappingTypes.ValidateValue(target, MappingTypes.ApplyDefaults(target, n.Value));
        }
        foreach (var selector in c.Selectors)
        {
            if (selector.WorkflowDefinitionId == Guid.Empty || selector.WorkflowVersionId == Guid.Empty || selector.KeyKind is not ("BusinessKey" or "CorrelationKey") || selector.MultipleMatches is not ("Attention" or "Latest")
                || selector.WindowSeconds is <= 0 or > 31536000 || selector.Status is not (null or "Pending" or "Running" or "Completed" or "Suspended" or "Failed" or "Cancelled")) throw new FormatException("Invalid related instance selector.");
            // Selectors may only depend on current-tree sources, never another related selection.
            foreach (var n in Nodes(selector.KeyValue).Concat(selector.StartedByUserId is null ? [] : Nodes(selector.StartedByUserId)))
                if (n.Source?.SelectorId is not null) throw new FormatException("Related selectors cannot depend on other selectors.");
            Expression(selector.KeyValue, new("string")); if (selector.StartedByUserId is not null) Expression(selector.StartedByUserId, new("string")); Policy(selector.Missing);
            if (selector.Missing?.Mode == "Fallback" && selector.Missing.Fallback.ValueKind != JsonValueKind.Null)
                throw new FormatException("Selector Fallback must use null to permit explicit per-source fallback; it cannot impersonate an instance.");
        }
        foreach (var binding in c.Inputs)
        {
            var schema = binding.Location == "body" && binding.Name == "$body" ? c.Endpoint?.Body
                : c.Endpoint?.Parameters.SingleOrDefault(p => p.Location == binding.Location && p.Name == binding.Name)?.Schema;
            if (schema is null) throw new FormatException("Unknown mapping destination."); Expression(binding.Value, schema);
        }
        foreach (var parameter in (c.Endpoint?.Parameters ?? []).Where(p => p.Schema.Required && p.Schema.Default.ValueKind == JsonValueKind.Undefined))
            if (!c.Inputs.Any(b => b.Location == parameter.Location && b.Name == parameter.Name)) throw new FormatException("Required parameter is not mapped: " + parameter.Name);
        if (c.Endpoint?.Body is { Required: true } body && body.Default.ValueKind == JsonValueKind.Undefined && !c.Inputs.Any(b => b.Location == "body" && b.Name == "$body")) throw new FormatException("Required body is not mapped.");
        foreach (var output in c.Outputs) { MappingTypes.ValidateSchema(output.Schema); Expression(output.Value, output.Schema, true); availableOutputs++; }
        foreach (var binding in c.StepBindings)
        {
            if (binding.Mode is not ("ReadOnly" or "Prefill")) throw new FormatException("Select ReadOnly or Prefill.");
            var field = catalog.Sources.SingleOrDefault(s => s.Reference.WorkflowVersionId == owner.WorkflowVersionId && s.Reference.NodeKey == owner.NodeKey && s.Reference.FormStepId == binding.FormStepId && s.Reference.FormId == binding.FormId && s.Reference.FormVersion == binding.FormVersion && s.Reference.FieldKey == binding.FieldKey);
            if (field is null || !MappingTypes.Compatible(binding.Schema, field.Schema)) throw new FormatException("Unknown or incompatible pinned Step field.");
            MappingTypes.ValidateSchema(binding.Schema); Expression(binding.Value, binding.Schema);
        }
        if (c.Callable && c.InvocationInputs is null) throw new FormatException("Callable events require an invocation input schema.");
        if (c.InvocationInputs is not null) MappingTypes.ValidateSchema(c.InvocationInputs);
        foreach (var action in c.Actions)
        {
            Policy(action.UnavailableTarget);
            if (action.UnavailableTarget?.Mode == "Fallback") throw new FormatException("An unavailable target cannot be replaced by a value.");
            var target = catalog.Targets.SingleOrDefault(t => t.Target == action.Target && t.Callable);
            if (target is null || action.Target == owner || action.Mode is not ("MapOnly" or "Run")) throw new FormatException("Select another callable event in this workflow tree and MapOnly or Run.");
            Expression(action.Inputs, target.Inputs, true);
        }
        var dependencies = (catalog.Dependencies ?? []).Where(e => e.Kind != "Graph" && e.From != owner).ToList();
        dependencies.AddRange(c.Actions.Where(a => a.Mode == "Run").Select(a => new MappingDependency(owner, a.Target, "Invocation")));
        foreach (var expression in c.Inputs.Select(b => b.Value).Concat(c.StepBindings.Select(b => b.Value)).Concat(c.Outputs.Select(o => o.Value)).Concat(c.Actions.Select(a => a.Inputs)).Concat(c.Selectors.Select(s => s.KeyValue)))
            dependencies.AddRange(Nodes(expression).Where(n => n.Source is { SelectorId: null, Kind: "EventOutput" or "RoutingOutput" or "FormField" } source
                && (source.WorkflowVersionId != owner.WorkflowVersionId || source.NodeKey != owner.NodeKey)).Select(n => new MappingDependency(owner, new(n.Source!.WorkflowDefinitionId, n.Source.WorkflowVersionId, n.Source.NodeKey), "Source")));
        foreach (var edge in dependencies.Where(e => e.From == owner))
            if (edge.To == owner || Reachable(edge.To, owner, edge.Kind, dependencies)) throw new FormatException("Mapping dependency or invocation cycle detected.");
        // A call is created after its caller's outputs commit: the callee depends on the caller,
        // not the reverse. Keep that prerequisite direction separate from recursive call edges.
        var prerequisites = dependencies.Select(e => e.Kind == "Invocation" ? new MappingDependency(e.To, e.From, "Source") : e).ToList();
        foreach (var edge in prerequisites.Where(e => e.From == owner || e.To == owner))
            if (Reachable(edge.To, edge.From, "Source", prerequisites)) throw new FormatException("Mapping completion prerequisite cycle detected.");
    }
    private static bool Reachable(CallableTarget from, CallableTarget to, string? kind, IReadOnlyList<MappingDependency> edges)
    {
        var seen = new HashSet<CallableTarget>(); var pending = new Stack<CallableTarget>(); pending.Push(from);
        while (pending.TryPop(out var node)) { if (!seen.Add(node)) continue; foreach (var edge in edges.Where(e => e.From == node && (kind is null || e.Kind == kind))) { if (edge.To == to) return true; pending.Push(edge.To); } }
        return false;
    }
    public static IEnumerable<TransformNode> Nodes(TransformNode node)
    {
        MappingEvaluator.ValidateTree(node);
        var pending = new Stack<TransformNode>(); pending.Push(node);
        while (pending.TryPop(out var n)) { yield return n; foreach (var a in n.Args ?? []) pending.Push(a); foreach (var p in n.Properties ?? []) pending.Push(p.Value); }
    }
    private static MappingSchema LiteralType(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Null) return new("null", Nullable: true);
        if (value.ValueKind == JsonValueKind.Array && value.GetArrayLength() == 0) throw new FormatException("An empty array literal needs a typed source or non-empty sample; use a whole typed array source.");
        var s = MappingTypes.Infer(value);
        return s.Type == "object" ? s with { Properties = value.EnumerateObject().ToDictionary(p => p.Name, p => LiteralType(p.Value) with { Required = true }) } : s;
    }
}
