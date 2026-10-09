using System.Net;
using System.Net.Http.Headers;
using FindThatBook.Infrastructure.OpenAi;
using Microsoft.Extensions.Options;

namespace FindThatBook.Infrastructure.Health;

/// <summary>Checks an OpenAI-compatible endpoint by listing its models. A server that has no <c>/models</c> route still counts as reachable.</summary>
/// <param name="clients">Factory for the HTTP client.</param>
/// <param name="options">OpenAI-compatible settings.</param>
public sealed class OpenAiCompatibleProbe(IHttpClientFactory clients, IOptions<OpenAiCompatibleOptions> options) : IHealthProbe
{
    /// <inheritdoc />
    public string Name => "openAiCompatible";

    /// <inheritdoc />
    public bool Critical => false;

    /// <inheritdoc />
    public Task<DependencyHealth> CheckAsync(CancellationToken ct) => ProbeRunner.RunAsync(Name, async token =>
    {
        var settings = options.Value;
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(settings.BaseUri, "models"));
        if (!string.IsNullOrWhiteSpace(settings.ApiKey))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);

        using var response = await clients.CreateClient(ProbeRunner.ClientName).SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        return response.StatusCode switch
        {
            _ when response.IsSuccessStatusCode => (HealthStatus.Ok, (string?)null),
            HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed
                => (HealthStatus.Ok, "reachable, but the server has no models route"),
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                => (HealthStatus.Down, $"Endpoint rejected the API key ({(int)response.StatusCode})"),
            HttpStatusCode.TooManyRequests => (HealthStatus.Degraded, "Endpoint is rate limiting requests (429)"),
            _ => (HealthStatus.Down, $"Endpoint returned {(int)response.StatusCode}"),
        };
    }, ct);
}
