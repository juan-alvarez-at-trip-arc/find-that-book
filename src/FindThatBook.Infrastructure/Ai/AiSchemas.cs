using System.Text.Json.Nodes;

namespace FindThatBook.Infrastructure.Ai;

/// <summary>Provider-neutral output schemas in standard JSON Schema. Clients must not mutate them.</summary>
public static class AiSchemas
{
    public static readonly JsonNode Query = JsonNode.Parse("""
        {"type":"object","properties":{
          "title":{"type":["string","null"]},
          "author":{"type":["string","null"]},
          "keywords":{"type":"array","items":{"type":"string"}}},
         "required":["keywords"]}
        """)!;

    public static readonly JsonNode Explanations = JsonNode.Parse("""
        {"type":"array","items":{"type":"object","properties":{
          "index":{"type":"integer"},"explanation":{"type":"string"}},
         "required":["index","explanation"]}}
        """)!;
}
