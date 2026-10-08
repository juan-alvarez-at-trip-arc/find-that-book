using System.Text.Json.Nodes;

namespace FindThatBook.Infrastructure.Ai;

public interface IAiClient
{
    /// <summary>Returns the model's JSON text. Throws <see cref="Application.Abstractions.AiUnavailableException"/> on any failure.</summary>
    Task<string> GenerateJsonAsync(string prompt, JsonNode schema, CancellationToken ct);
}
