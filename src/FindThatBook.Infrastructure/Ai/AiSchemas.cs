using System.Text.Json.Nodes;

namespace FindThatBook.Infrastructure.Ai;

/// <summary>Provider-neutral output schemas in standard JSON Schema. Clients must not mutate them.</summary>
public static class AiSchemas
{
    /// <summary>Shape of the query-extraction reply: an object with a nullable <c>title</c>, a nullable <c>author</c> and a <c>keywords</c> array.</summary>
    public static readonly JsonNode Query = JsonNode.Parse("""
        {"type":"object","properties":{
          "title":{"type":["string","null"]},
          "author":{"type":["string","null"]},
          "keywords":{"type":"array","items":{"type":"string"}}},
         "required":["keywords"]}
        """)!;

    /// <summary>Shape of the explanation reply: an array with one object per result holding its <c>index</c> and <c>explanation</c> text.</summary>
    public static readonly JsonNode Explanations = JsonNode.Parse("""
        {"type":"array","items":{"type":"object","properties":{
          "index":{"type":"integer"},"explanation":{"type":"string"}},
         "required":["index","explanation"]}}
        """)!;
}
