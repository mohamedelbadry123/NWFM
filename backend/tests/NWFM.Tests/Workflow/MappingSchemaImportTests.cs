using System.Text.Json;
using FluentAssertions;
using Workflow.Application.Mapping;

namespace NWFM.Tests.Modules.Workflow;

public sealed class MappingSchemaImportTests
{
    private static EndpointDefinition ImportSchema(string schema, string parameters = "", string pathParameters = "") => MappingSchemaImport.Import(new(
        "{\"openapi\":\"3.0.3\",\"paths\":{\"/items\":{" + pathParameters + "\"post\":{" + parameters + "\"responses\":{\"200\":{\"content\":{\"application/json\":{\"schema\":" + schema + "}}}}}}}}", "/items", "POST"));
    [Fact]
    public void OperationParametersOverridePathByLocationAndNameButDuplicatesFail()
    {
        const string inherited = "\"parameters\":[{\"in\":\"query\",\"name\":\"count\",\"schema\":{\"type\":\"string\"}},{\"in\":\"header\",\"name\":\"count\",\"schema\":{\"type\":\"string\"}}],";
        const string parameter = "{\"in\":\"query\",\"name\":\"count\",\"required\":true,\"schema\":{\"type\":\"integer\",\"default\":5}}";
        var result = ImportSchema("{\"type\":\"boolean\"}", "\"parameters\":[" + parameter + "],", inherited);
        result.Parameters.Should().HaveCount(2);
        var query = result.Parameters.Single(p => p.Location == "query").Schema;
        query.Type.Should().Be("integer"); query.Required.Should().BeTrue(); query.Default.GetInt32().Should().Be(5);
        foreach (var atOperation in new[] { true, false })
        {
            var duplicate = "\"parameters\":[" + parameter + "," + parameter + "],";
            Action act = () => ImportSchema("{\"type\":\"boolean\"}", atOperation ? duplicate : "", atOperation ? "" : duplicate);
            act.Should().Throw<FormatException>().WithMessage("*Duplicate parameter*");
        }
        Action duplicateKey = () => ImportSchema("{\"type\":\"object\",\"properties\":{\"x\":{\"type\":\"string\"},\"x\":{\"type\":\"integer\"}}}");
        duplicateKey.Should().Throw<FormatException>().WithMessage("*Duplicate OpenAPI*");
    }
    [Theory]
    [InlineData("")]
    [InlineData(",\"additionalProperties\":true")]
    public void OpenApiOpenObjectsRetainUnknownBoundedJson(string additional)
    {
        var schema = ImportSchema("{\"type\":\"object\",\"properties\":{\"known\":{\"type\":\"string\"}}" + additional + "}").Response!;
        schema.AdditionalProperties.Should().BeTrue();
        var value = JsonSerializer.SerializeToElement(new { known = "x", extra = new { array = new[] { 1, 2 } } });
        MappingTypes.ValidateValue(schema, value);
        MappingTypes.Compatible(MappingTypes.Infer(value), schema).Should().BeTrue();
        Action closed = () => MappingTypes.ValidateValue(schema with { AdditionalProperties = false }, value);
        closed.Should().Throw<FormatException>();
        Action excessive = () => MappingTypes.ValidateValue(schema, JsonSerializer.SerializeToElement(new { extra = new int[1001] }));
        excessive.Should().Throw<FormatException>();
        MappingTypes.Compatible(schema, schema with { AdditionalProperties = false }).Should().BeFalse();
    }
    [Fact]
    public void DictionarySchemasValidateValuesPointersDefaultsAndCompatibility()
    {
        var schema = ImportSchema("{\"type\":\"object\",\"additionalProperties\":{\"type\":\"number\"}}").Response!;
        MappingTypes.ValidateValue(schema, JsonSerializer.SerializeToElement(new { a = 1, b = 2.5 }));
        MappingTypes.SelectSchema(schema, "/dynamic").Type.Should().Be("number");
        MappingTypes.Compatible(MappingTypes.Infer(JsonSerializer.SerializeToElement(new { x = 2 })), schema).Should().BeTrue();
        MappingTypes.Compatible(schema with { AdditionalPropertySchema = null }, schema).Should().BeFalse();
        Action invalid = () => MappingTypes.ValidateValue(schema, JsonSerializer.SerializeToElement(new { x = "2" })); invalid.Should().Throw<FormatException>();
        var closed = ImportSchema("{\"type\":\"object\",\"additionalProperties\":false}").Response!;
        closed.AdditionalProperties.Should().BeFalse();
        var manual = new MappingSchema("object", Properties: []); manual.AdditionalProperties.Should().BeFalse();
    }
    [Fact]
    public void InferenceRejectsUnusableNamesDuplicateKeysAndSchemaBudgets()
    {
        foreach (var json in new[] { "{\"\":1}", "{\"bad\\u0001key\":1}", "{\"x\":1,\"x\":2}", "{\"" + new string('x', 201) + "\":1}" })
        {
            using var document = JsonDocument.Parse(json); Action act = () => MappingTypes.Infer(document.RootElement); act.Should().Throw<FormatException>();
        }
        Action broad = () => MappingTypes.Infer(JsonSerializer.SerializeToElement(Enumerable.Range(0, 500).ToDictionary(i => "key" + i, i => i)));
        broad.Should().Throw<FormatException>().WithMessage("*node/depth*");
        var nested = JsonSerializer.SerializeToElement(1);
        for (var i = 0; i < 18; i++) nested = JsonSerializer.SerializeToElement(new { child = nested });
        Action deep = () => MappingTypes.Infer(nested); deep.Should().Throw<FormatException>();
    }
    private const string Document = """
        {"openapi":"3.0.3","paths":{"/items/{id}":{"post":{"parameters":[{"in":"path","name":"id","required":true,"schema":{"type":"string"}},{"in":"query","name":"count","schema":{"type":"integer","default":5}}],"requestBody":{"required":true,"content":{"application/json":{"schema":{"$ref":"#/components/schemas/Input"}}}},"responses":{"200":{"description":"ok","content":{"application/json":{"schema":{"type":"object","properties":{"done":{"type":"boolean"}}}}}}}}}},"components":{"schemas":{"Input":{"type":"object","required":["name"],"properties":{"name":{"type":"string"},"note":{"type":"string","nullable":true},"rows":{"type":"array","items":{"type":"number"}}}}}}}
        """;
    [Fact]
    public void ImportsSelectedOperationInternalRefsRequiredNullableAndDefaults()
    {
        var result = MappingSchemaImport.Import(new(Document, "/items/{id}", "POST"));
        result.Method.Should().Be("POST"); result.Body!.Required.Should().BeTrue(); result.Body.Properties!["name"].Required.Should().BeTrue();
        result.Body.Properties["note"].Nullable.Should().BeTrue(); result.Body.Properties["rows"].Items!.Type.Should().Be("number");
        result.Parameters[1].Schema.Default.GetInt32().Should().Be(5); result.Response!.Inferred.Should().BeFalse();
    }
    [Theory]
    [InlineData("https://example.invalid/schema")]
    [InlineData("#/missing")]
    public void RejectsExternalOrUnresolvedReferences(string reference)
    { Action act = () => MappingSchemaImport.Import(new(Document.Replace("#/components/schemas/Input", reference), "/items/{id}", "POST")); act.Should().Throw<FormatException>(); }
    [Fact]
    public void RejectsRecursiveReferencesUnsupportedSemanticsAndMalformedSchemas()
    {
        foreach (var text in new[] { Document.Replace("\"name\":{\"type\":\"string\"}", "\"name\":{\"$ref\":\"#/components/schemas/Input\"}"),
            Document.Replace("\"type\":\"string\"", "\"type\":\"string\",\"pattern\":\"[a-z]\""), Document.Replace("\"required\":[\"name\"]", "\"required\":[\"missing\"]") })
        { Action act = () => MappingSchemaImport.Import(new(text, "/items/{id}", "POST")); act.Should().Throw<FormatException>(); }
        Action malformed = () => MappingTypes.ValidateSchema(new("array")); malformed.Should().Throw<FormatException>();
    }
    [Fact]
    public void ResponseInferenceIsExplicitAndDoesNotInventRequiredFields()
    {
        using var document = JsonDocument.Parse("{\"id\":1,\"items\":[{\"name\":\"x\"}]}");
        var schema = MappingTypes.Infer(document.RootElement);
        schema.Inferred.Should().BeTrue(); schema.Properties!["id"].Required.Should().BeFalse(); schema.Properties["items"].Items!.Properties!["name"].Inferred.Should().BeTrue();
        Action empty = () => MappingTypes.Infer(JsonSerializer.SerializeToElement(Array.Empty<string>())); empty.Should().Throw<FormatException>();
    }
}
