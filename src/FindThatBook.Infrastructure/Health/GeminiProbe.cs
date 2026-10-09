using System.Net;
using FindThatBook.Infrastructure.Gemini;
using Microsoft.Extensions.Options;

namespace FindThatBook.Infrastructure.Health;

/// <summary>Checks Gemini by reading the configured model's metadata. The call needs the API key but uses no tokens.</summary>
/// <param name="clients">Factory for the HTTP client.</param>
/// <param name="options">Gemini settings.</param>
public sealed class GeminiProbe(IHttpClientFactory clients, IOptions<GeminiOptions> options) : IHealthProbe
{
    /// <inheritdoc />
    public string Name => "gemini";

    /// <inheritdoc />
    public bool Critical => false;

    /// <inheritdoc />
    public Task<DependencyHealth> CheckAsync(CancellationToken ct) => ProbeRunner.RunAsync(Name, async token =>
    {
        var settings = options.Value;
        if (string.IsNullOrWhiteSpace(settings.ApiKey))
            return (HealthStatus.Degraded, "API key is not configured; the non-AI fallback is used");

        var model = string.IsNullOrWhiteSpace(settings.Model) ? GeminiOptions.DefaultModel : settings.Model.Trim();
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(ProbeRunner.BaseUri(GeminiOptions.ApiBaseUrl), $"v1beta/models/{model}"));
        request.Headers.Add("x-goog-api-key", settings.ApiKey);

        using var response = await clients.CreateClient(ProbeRunner.ClientName).SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        return response.StatusCode switch
        {
            _ when response.IsSuccessStatusCode => (HealthStatus.Ok, (string?)null),
            HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                => (HealthStatus.Down, $"Gemini rejected the API key ({(int)response.StatusCode})"),
            HttpStatusCode.NotFound => (HealthStatus.Down, $"Gemini model '{model}' was not found"),
            HttpStatusCode.TooManyRequests => (HealthStatus.Degraded, "Gemini is rate limiting requests (429)"),
            _ => (HealthStatus.Down, $"Gemini returned {(int)response.StatusCode}"),
        };
    }, ct);
}
