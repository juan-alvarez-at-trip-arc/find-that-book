using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using FindThatBook.Application.Abstractions;
using FindThatBook.Infrastructure.Ai;
using Microsoft.Extensions.Options;

namespace FindThatBook.Infrastructure.OpenAi;

/// <summary>Talks to any OpenAI-compatible <c>/chat/completions</c> endpoint (Groq, Cerebras, Ollama, OpenRouter, Mistral).</summary>
public sealed class OpenAiCompatibleClient(HttpClient http, IOptions<OpenAiCompatibleOptions> options) : IAiClient
{
    public async Task<string> GenerateJsonAsync(string prompt, JsonNode schema, CancellationToken ct)
    {
        var settings = options.Value;

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
                        + "The object must conform to this JSON Schema:\n" + effectiveSchema.ToJsonString(),
                },
                new JsonObject { ["role"] = "user", ["content"] = prompt }),
            ["response_format"] = new JsonObject { ["type"] = "json_object" },
            ["temperature"] = 0,
            ["stream"] = false, // some gateways stream by default, which this client cannot parse
        };

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

    private static string Unwrap(string text)
    {
        using var doc = JsonDocument.Parse(text);
        if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty("result", out var result))
            throw new AiUnavailableException("AI provider reply is missing the \"result\" property.");
        return result.GetRawText();
    }
}
