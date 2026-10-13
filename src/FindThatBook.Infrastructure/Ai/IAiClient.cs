using System.Text.Json.Nodes;

namespace FindThatBook.Infrastructure.Ai;

/// <summary>A request to the model, split so that the instructions we wrote and the data we do not control travel in different roles.</summary>
/// <param name="Instructions">What the model must do, written by this application. Providers send it as the system instruction.</param>
/// <param name="Data">The untrusted material the instructions refer to, such as the reader's text or catalog records. Providers send it as the user message.</param>
public sealed record AiPrompt(string Instructions, string Data);

/// <summary>Provider-neutral client that asks an AI model for JSON output, so the parser and explanation writer work with Gemini or any OpenAI-compatible endpoint (selected by <c>Ai:Provider</c>).</summary>
public interface IAiClient
{
    /// <summary>Sends a prompt to the model in JSON mode and returns the JSON text of its reply.</summary>
    /// <param name="prompt">The instructions and the untrusted data to send, kept in separate roles.</param>
    /// <param name="schema">The expected output shape as standard JSON Schema. Implementations must not mutate it.</param>
    /// <param name="ct">Token used to cancel the request.</param>
    /// <returns>The model's reply as JSON text that conforms to <paramref name="schema"/>.</returns>
    /// <exception cref="Application.Abstractions.AiUnavailableException">Any failure, including a missing key, a non-success status, a timeout, or an unexpected payload.</exception>
    Task<string> GenerateJsonAsync(AiPrompt prompt, JsonNode schema, CancellationToken ct);
}
