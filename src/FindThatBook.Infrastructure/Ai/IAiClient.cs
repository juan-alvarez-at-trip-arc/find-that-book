using System.Text.Json.Nodes;

namespace FindThatBook.Infrastructure.Ai;

/// <summary>Provider-neutral client that asks an AI model for JSON output, so the parser and explanation writer work with Gemini or any OpenAI-compatible endpoint (selected by <c>Ai:Provider</c>).</summary>
public interface IAiClient
{
    /// <summary>Sends a prompt to the model in JSON mode and returns the JSON text of its reply.</summary>
    /// <param name="prompt">The full prompt text to send.</param>
    /// <param name="schema">The expected output shape as standard JSON Schema. Implementations must not mutate it.</param>
    /// <param name="ct">Token used to cancel the request.</param>
    /// <returns>The model's reply as JSON text that conforms to <paramref name="schema"/>.</returns>
    /// <exception cref="Application.Abstractions.AiUnavailableException">Any failure, including a missing key, a non-success status, a timeout, or an unexpected payload.</exception>
    Task<string> GenerateJsonAsync(string prompt, JsonNode schema, CancellationToken ct);
}
