using FindThatBook.Infrastructure.OpenLibrary;
using Microsoft.Extensions.Options;

namespace FindThatBook.Infrastructure.Health;

/// <summary>Checks that Open Library answers a one-result search. It skips the shared request gate so a probe never takes a search's slot, but it respects an active 429 cooldown.</summary>
/// <param name="clients">Factory for the HTTP client.</param>
/// <param name="options">Open Library settings.</param>
/// <param name="gate">Shared gate, read only to detect a cooldown.</param>
public sealed class OpenLibraryProbe(IHttpClientFactory clients, IOptions<OpenLibraryOptions> options, OpenLibraryGate gate) : IHealthProbe
{
    /// <inheritdoc />
    public string Name => "openLibrary";

    /// <inheritdoc />
    public bool Critical => true;

    /// <inheritdoc />
    public Task<DependencyHealth> CheckAsync(CancellationToken ct) => ProbeRunner.RunAsync(Name, async token =>
    {
        if (gate.IsCoolingDown(out var remaining))
            return (HealthStatus.Down, $"Open Library asked us to slow down; cooling down for {(int)Math.Ceiling(remaining.TotalSeconds)}s");

        var settings = options.Value;
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(ProbeRunner.BaseUri(settings.BaseUrl), "search.json?q=test&limit=1&fields=key"));
        request.Headers.UserAgent.ParseAdd(settings.UserAgent);

        using var response = await clients.CreateClient(ProbeRunner.ClientName).SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        return response.IsSuccessStatusCode
            ? (HealthStatus.Ok, (string?)null)
            : (HealthStatus.Down, $"Open Library returned {(int)response.StatusCode}");
    }, ct);
}
