using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using FindThatBook.Application.Abstractions;
using FindThatBook.Infrastructure.Ai;
using Microsoft.Extensions.Options;

namespace FindThatBook.Infrastructure.OpenAi;

/// <summary>Talks to any OpenAI-compatible <c>/chat/completions</c> endpoint (Groq, Cerebras, Ollama, OpenRouter, Mistral) in JSON mode, putting the JSON Schema in the system message and wrapping non-object schemas as <c>{"result": ...}</c>.</summary>
/// <param name="http">HTTP client whose base address points at the provider's API root.</param>
/// <param name="options">Provider settings: API key, base URL and model name.</param>
/// <param name="generationOptions">Temperature, top-k, top-p, seed and reasoning effort from the <c>Ai</c> configuration section. When null, the defaults apply: temperature 0 and nothing else.</param>
public sealed class OpenAiCompatibleClient(HttpClient http, IOptions<OpenAiCompatibleOptions> options, IOptions<AiGenerationOptions>? generationOptions = null) : IAiClient
{
    /// <summary>Posts a <c>chat/completions</c> request with <c>response_format</c> set to <c>json_object</c>, the configured temperature and, when set, <c>top_p</c>, <c>top_k</c>, <c>seed</c> and <c>reasoning_effort</c>. Array-shaped schemas are wrapped in an object because JSON mode needs an object at the root.</summary>
    /// <param name="prompt">The instructions, sent in the system message with the schema, and the untrusted data, sent alone as the user message.</param>
    /// <param name="schema">The expected output shape as standard JSON Schema. It is cloned, never mutated.</param>
    /// <param name="ct">Token used to cancel the request.</param>
    /// <returns>The JSON text of the first choice's message, unwrapped when the schema was wrapped.</returns>
    /// <exception cref="AiUnavailableException">The request fails or times out, the status is not a success, the reply has no content or an unexpected shape, or a wrapped reply lacks the <c>result</c> property.</exception>
    public async Task<string> GenerateJsonAsync(AiPrompt prompt, JsonNode schema, CancellationToken ct)
    {
        var settings = options.Value;
        var gen = generationOptions?.Value ?? new AiGenerationOptions();

        // json_object mode needs an object at the root, so other roots are wrapped as {"result": ...}.
        var wrapped = !(schema["type"] is JsonValue type && type.TryGetValue<string>(out var name) && name == "object");
        var effectiveSchema = wrapped
            ? new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject { ["result"] = schema.DeepClone() },
                ["required"] = new JsonArray("result"),
            }
            : schema.DeepClone();

        var body = new JsonObject
        {
            ["model"] = string.IsNullOrWhiteSpace(settings.Model) ? OpenAiCompatibleOptions.DefaultModel : settings.Model.Trim(),
            ["messages"] = new JsonArray(
                new JsonObject
                {
                    ["role"] = "system",
                    ["content"] = "You are a JSON API. Reply with exactly one JSON object and nothing else: no prose, no markdown. "
                        + "The object must conform to this JSON Schema:\n" + effectiveSchema.ToJsonString()
                        + "\n\n" + prompt.Instructions,
                },
                new JsonObject { ["role"] = "user", ["content"] = prompt.Data }),
            ["response_format"] = new JsonObject { ["type"] = "json_object" },
            ["temperature"] = gen.EffectiveTemperature,
            ["stream"] = false, // some gateways stream by default, which this client cannot parse
        };
        if (gen.EffectiveTopP is { } topP) body["top_p"] = topP;
        if (gen.EffectiveTopK is { } topK) body["top_k"] = topK;
        if (gen.EffectiveSeed is { } seed) body["seed"] = seed;
        if (gen.EffectiveReasoningEffort is { } effort) body["reasoning_effort"] = effort;

        using var request = new HttpRequestMessage(HttpMethod.Post, "chat/completions") { Content = JsonContent.Create(body) };
        if (!string.IsNullOrWhiteSpace(settings.ApiKey))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey.Trim());

        try
        {
            using var response = await http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
                throw new AiUnavailableException($"AI provider returned {(int)response.StatusCode}.");

            var json = await response.Content.ReadFromJsonAsync<JsonNode>(ct);
            var content = json?["choices"] is JsonArray { Count: > 0 } choices ? choices[0]?["message"]?["content"] : null;
            var text = content is JsonValue value && value.TryGetValue<string>(out var s) ? s : null;
            if (string.IsNullOrWhiteSpace(text))
                throw new AiUnavailableException("AI provider returned no message content.");

            return wrapped ? Unwrap(text) : text;
        }
        catch (HttpRequestException ex)
        {
            throw new AiUnavailableException("AI provider request failed.", ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new AiUnavailableException("AI provider timed out.", ex);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            throw new AiUnavailableException("AI provider returned an unexpected payload.", ex);
        }
    }

    /// <summary>Extracts the <c>result</c> property from a wrapped reply so callers see the JSON shape they asked for.</summary>
    /// <param name="text">The reply text, expected to be a JSON object with a <c>result</c> property.</param>
    /// <returns>The raw JSON of the <c>result</c> property.</returns>
    /// <exception cref="AiUnavailableException">The reply is not an object or has no <c>result</c> property.</exception>
    private static string Unwrap(string text)
    {
        using var doc = JsonDocument.Parse(text);
        if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty("result", out var result))
            throw new AiUnavailableException("AI provider reply is missing the \"result\" property.");
        return result.GetRawText();
    }
}
