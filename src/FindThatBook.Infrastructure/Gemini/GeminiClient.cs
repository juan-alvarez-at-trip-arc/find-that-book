using FindThatBook.Infrastructure.Ai;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using FindThatBook.Application.Abstractions;
using Microsoft.Extensions.Options;

namespace FindThatBook.Infrastructure.Gemini;

/// <summary>AI client for the Gemini <c>generateContent</c> API, sending the prompt with a converted <c>responseSchema</c> and returning the JSON text from the first candidate.</summary>
/// <param name="http">HTTP client whose base address points at the Gemini API.</param>
/// <param name="options">Gemini settings: API key and model name.</param>
/// <param name="generationOptions">Temperature, top-k, top-p, seed and reasoning effort from the <c>Ai</c> configuration section. When null, the defaults apply: temperature 0 and nothing else.</param>
public sealed class GeminiClient(HttpClient http, IOptions<GeminiOptions> options, IOptions<AiGenerationOptions>? generationOptions = null) : IAiClient
{
    /// <summary>Gets the first element of a JSON array node.</summary>
    /// <param name="node">The node to inspect, expected to be a JSON array.</param>
    /// <returns>The first element, or <c>null</c> when the node is not an array or is empty.</returns>
    private static JsonNode? First(JsonNode? node) => node is JsonArray { Count: > 0 } array ? array[0] : null;

    /// <summary>Calls Gemini with JSON response mode and the schema converted to Gemini's dialect, using the configured temperature and, when set, top-k, top-p, seed and reasoning effort (sent as <c>thinkingConfig.thinkingLevel</c>). The instructions go in <c>systemInstruction</c> and the data in the user content.</summary>
    /// <param name="prompt">The instructions and the untrusted data to send.</param>
    /// <param name="schema">The expected output shape as standard JSON Schema.</param>
    /// <param name="ct">Token used to cancel the request.</param>
    /// <returns>The JSON text of the first candidate's first part.</returns>
    /// <exception cref="AiUnavailableException">The API key is missing, the request fails or times out, the status is not a success, or the response has no text or an unexpected shape.</exception>
    public async Task<string> GenerateJsonAsync(AiPrompt prompt, JsonNode schema, CancellationToken ct)
    {
        var settings = options.Value;
        if (string.IsNullOrWhiteSpace(settings.ApiKey))
            throw new AiUnavailableException("Gemini API key is not configured.");

        var gen = generationOptions?.Value ?? new AiGenerationOptions();
        var generation = new JsonObject
        {
            ["responseMimeType"] = "application/json",
            ["responseSchema"] = GeminiSchema.FromJsonSchema(schema),
            ["temperature"] = gen.EffectiveTemperature,
        };
        if (gen.EffectiveTopK is { } topK) generation["topK"] = topK;
        if (gen.EffectiveTopP is { } topP) generation["topP"] = topP;
        if (gen.EffectiveSeed is { } seed) generation["seed"] = seed;
        if (gen.EffectiveReasoningEffort is { } effort) generation["thinkingConfig"] = new JsonObject { ["thinkingLevel"] = effort };

        var body = new JsonObject
        {
            ["systemInstruction"] = new JsonObject
            {
                ["parts"] = new JsonArray(new JsonObject { ["text"] = prompt.Instructions }),
            },
            ["contents"] = new JsonArray(new JsonObject
            {
                ["parts"] = new JsonArray(new JsonObject { ["text"] = prompt.Data }),
            }),
            ["generationConfig"] = generation,
        };

        var model = string.IsNullOrWhiteSpace(settings.Model) ? GeminiOptions.DefaultModel : settings.Model.Trim();
        using var request = new HttpRequestMessage(HttpMethod.Post, $"v1beta/models/{model}:generateContent")
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Add("x-goog-api-key", settings.ApiKey);

        try
        {
            using var response = await http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
                throw new AiUnavailableException($"Gemini returned {(int)response.StatusCode}.");

            var json = await response.Content.ReadFromJsonAsync<JsonNode>(ct);
            var text = First(json?["candidates"])?["content"]?["parts"] is { } parts
                ? First(parts)?["text"]?.GetValue<string>()
                : null;
            if (string.IsNullOrWhiteSpace(text))
                throw new AiUnavailableException("Gemini returned no text.");
            return text;
        }
        catch (HttpRequestException ex)
        {
            throw new AiUnavailableException("Gemini request failed.", ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new AiUnavailableException("Gemini timed out.", ex);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            throw new AiUnavailableException("Gemini returned an unexpected payload.", ex);
        }
    }
}
