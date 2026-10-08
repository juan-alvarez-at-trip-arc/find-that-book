using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using FindThatBook.Application.Abstractions;
using FindThatBook.Application.Models;
using FindThatBook.Infrastructure.Gemini;

namespace FindThatBook.Tests.Support;

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
        string? id = null)
        => new(id ?? "OL" + Math.Abs(title.GetHashCode()) + "W", title, primary, contributors ?? [], year, editions, null, enriched);
}

public sealed class FakeGeminiClient(Func<string, string> respond) : IGeminiClient
{
    public Task<string> GenerateJsonAsync(string prompt, JsonNode schema, CancellationToken ct)
        => Task.FromResult(respond(prompt));
}

public static class Q
{
    public static ParsedQuery Make(string? title, string? author, params string[] keywords) => new(title, author, keywords);
}
