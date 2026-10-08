using System.Text.Json.Nodes;
using FindThatBook.Infrastructure.Ai;
using FindThatBook.Infrastructure.Gemini;

namespace FindThatBook.Tests;

public class GeminiSchemaTests
{
    // The exact schemas Gemini was sent before schemas became provider-neutral.
    private const string OriginalQuerySchema = """
        {"type":"OBJECT","properties":{
          "title":{"type":"STRING","nullable":true},
          "author":{"type":"STRING","nullable":true},
          "keywords":{"type":"ARRAY","items":{"type":"STRING"}}},
         "required":["keywords"]}
        """;

    private const string OriginalExplanationSchema = """
        {"type":"ARRAY","items":{"type":"OBJECT","properties":{
          "index":{"type":"INTEGER"},"explanation":{"type":"STRING"}},
         "required":["index","explanation"]}}
        """;

    [Fact]
    public void Query_schema_converts_to_the_original_gemini_schema()
    {
        var converted = GeminiSchema.FromJsonSchema(AiSchemas.Query);

        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(OriginalQuerySchema), converted), converted.ToJsonString());
    }

    [Fact]
    public void Explanation_schema_converts_to_the_original_gemini_schema()
    {
        var converted = GeminiSchema.FromJsonSchema(AiSchemas.Explanations);

        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(OriginalExplanationSchema), converted), converted.ToJsonString());
    }

    [Fact]
    public void Conversion_does_not_mutate_the_input()
    {
        var before = AiSchemas.Query.ToJsonString();

        GeminiSchema.FromJsonSchema(AiSchemas.Query);

        Assert.Equal(before, AiSchemas.Query.ToJsonString());
    }

    [Fact]
    public void Neutral_schemas_are_standard_json_schema()
    {
        Assert.Equal("object", AiSchemas.Query["type"]!.GetValue<string>());
        Assert.Equal("array", AiSchemas.Explanations["type"]!.GetValue<string>());
    }
}
