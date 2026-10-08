using FindThatBook.Infrastructure.Ai;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using FindThatBook.Application.Abstractions;
using Microsoft.Extensions.Options;

namespace FindThatBook.Infrastructure.Gemini;

public sealed class GeminiClient(HttpClient http, IOptions<GeminiOptions> options) : IAiClient
{
    private static JsonNode? First(JsonNode? node) => node is JsonArray { Count: > 0 } array ? array[0] : null;

    public async Task<string> GenerateJsonAsync(string prompt, JsonNode schema, CancellationToken ct)
    {
        var settings = options.Value;
        if (string.IsNullOrWhiteSpace(settings.ApiKey))
            throw new AiUnavailableException("Gemini API key is not configured.");

        var body = new JsonObject
        {
            ["contents"] = new JsonArray(new JsonObject
            {
                ["parts"] = new JsonArray(new JsonObject { ["text"] = prompt }),
            }),
            ["generationConfig"] = new JsonObject
            {
                ["responseMimeType"] = "application/json",
                ["responseSchema"] = GeminiSchema.FromJsonSchema(schema),
                ["temperature"] = 0,
            },
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
