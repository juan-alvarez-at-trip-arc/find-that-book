using FindThatBook.Infrastructure.Ai;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using FindThatBook.Application.Abstractions;
using FindThatBook.Application.Models;
using FindThatBook.Infrastructure.Gemini;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace FindThatBook.Tests.Support;

public sealed class RemoteIpFilter(string remote, string local) : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use((context, nextMiddleware) =>
        {
            context.Connection.RemoteIpAddress = IPAddress.Parse(remote);
            context.Connection.LocalIpAddress = IPAddress.Parse(local);
            return nextMiddleware(context);
        });
        next(app);
    };
}

public sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    public List<string> Requests { get; } = new();

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        lock (Requests) Requests.Add(request.RequestUri!.PathAndQuery);
        return Task.FromResult(respond(request));
    }

    public static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK)
        => new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
}

public sealed class FakeCatalog(Func<IReadOnlyList<ParsedQuery>, IReadOnlyList<CatalogCandidate>> find) : IBookCatalog
{
    public Task<IReadOnlyList<CatalogCandidate>> FindAsync(IReadOnlyList<ParsedQuery> hypotheses, CancellationToken ct)
        => Task.FromResult(find(hypotheses));
}

public static class Books
{
    public static CatalogCandidate Make(
        string title,
        string[] primary,
        string[]? contributors = null,
        int? year = null,
        int editions = 1,
        bool enriched = true,
        string? id = null,
        string[]? altTitles = null,
        string[]? aliases = null)
        => new(id ?? "OL" + Math.Abs(title.GetHashCode()) + "W", title, primary, contributors ?? [], year, editions, null, enriched, altTitles ?? [], aliases ?? []);
}

public sealed class FakeAiClient(Func<string, string> respond) : IAiClient
{
    public Task<string> GenerateJsonAsync(AiPrompt prompt, JsonNode schema, CancellationToken ct)
        => Task.FromResult(respond(prompt.Instructions + "\n" + prompt.Data));
}

public sealed class AsyncStubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => respond(request, ct);
}

public sealed class FakeClock(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;
    public override DateTimeOffset GetUtcNow() => _now;
    public void Advance(TimeSpan by) => _now += by;
}

public static class Q
{
    public static ParsedQuery Make(string? title, string? author, params string[] keywords) => new(title, author, keywords);

    /// <summary>A hypothesis the heuristic parser produced by guessing, as opposed to one the AI extracted.</summary>
    public static ParsedQuery Guess(string? title, string? author, params string[] keywords) => new(title, author, keywords, Guessed: true);
}

public sealed class PromptAiClient(Func<AiPrompt, string> respond) : IAiClient
{
    public Task<string> GenerateJsonAsync(AiPrompt prompt, JsonNode schema, CancellationToken ct)
        => Task.FromResult(respond(prompt));
}
