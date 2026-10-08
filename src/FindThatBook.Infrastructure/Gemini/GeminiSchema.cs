using System.Text.Json.Nodes;

namespace FindThatBook.Infrastructure.Gemini;

/// <summary>Converts standard JSON Schema into the dialect Gemini's <c>responseSchema</c> expects.</summary>
public static class GeminiSchema
{
    /// <summary>Converts a standard JSON Schema into the schema dialect Gemini accepts, without modifying the input.</summary>
    /// <param name="schema">The standard JSON Schema to convert.</param>
    /// <returns>A converted copy with upper-case type names and <c>nullable</c> in place of <c>"null"</c> type unions.</returns>
    public static JsonNode FromJsonSchema(JsonNode schema) => Convert(schema)!;

    /// <summary>Recursively converts a JSON node, rewriting schema objects and cloning other values.</summary>
    /// <param name="node">The node to convert.</param>
    /// <returns>The converted node, or <c>null</c> when <paramref name="node"/> is <c>null</c>.</returns>
    private static JsonNode? Convert(JsonNode? node) => node switch
    {
        JsonObject obj => ConvertSchema(obj),
        JsonArray array => new JsonArray(array.Select(Convert).ToArray()),
        null => null,
        _ => node.DeepClone(),
    };

    /// <summary>Converts one schema object, rewriting its <c>type</c> and recursing into <c>properties</c> and other nested schemas.</summary>
    /// <param name="schema">The schema object to convert.</param>
    /// <returns>A new schema object in Gemini's dialect.</returns>
    private static JsonObject ConvertSchema(JsonObject schema)
    {
        var result = new JsonObject();
        foreach (var (key, value) in schema)
        {
            switch (key)
            {
                case "type":
                    ApplyType(result, value);
                    break;
                case "properties" when value is JsonObject properties:
                    var converted = new JsonObject();
                    foreach (var (name, property) in properties) converted[name] = Convert(property);
                    result[key] = converted;
                    break;
                default:
                    result[key] = Convert(value);
                    break;
            }
        }
        return result;
    }

    /// <summary>Writes the Gemini form of a <c>type</c> value, upper-casing the name and turning a <c>"null"</c> union member into <c>nullable: true</c>.</summary>
    /// <param name="target">The converted schema object to write into.</param>
    /// <param name="type">The original <c>type</c> value, either a single name or an array of names.</param>
    private static void ApplyType(JsonObject target, JsonNode? type)
    {
        if (type is JsonArray types)
        {
            var names = types.Select(t => t?.GetValue<string>()).OfType<string>().ToList();
            var main = names.FirstOrDefault(n => n != "null");
            if (main is not null) target["type"] = main.ToUpperInvariant();
            if (names.Contains("null")) target["nullable"] = true;
        }
        else if (type is not null)
        {
            target["type"] = type.GetValue<string>().ToUpperInvariant();
        }
    }
}
