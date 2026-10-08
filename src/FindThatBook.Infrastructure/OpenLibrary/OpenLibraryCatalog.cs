using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using FindThatBook.Application.Abstractions;
using FindThatBook.Application.Models;
using FindThatBook.Application.Text;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FindThatBook.Infrastructure.OpenLibrary;

public sealed class OpenLibraryCatalog(
    HttpClient http,
    IMemoryCache cache,
    ILogger<OpenLibraryCatalog> log,
    IOptions<OpenLibraryOptions> options) : IBookCatalog
{
    private const string SearchFields = "key,title,author_name,author_key,first_publish_year,edition_count,cover_i";
    private const int DocsPerSearch = 5;
    private const int MaxEnriched = 12;
    private const int EnrichmentConcurrency = 4;
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromHours(1);

    private sealed record SearchDoc(
        string WorkId, string Title, IReadOnlyList<string> AuthorNames,
        int? FirstPublishYear, int EditionCount, int? CoverId);

    public async Task<IReadOnlyList<CatalogCandidate>> FindAsync(IReadOnlyList<ParsedQuery> hypotheses, CancellationToken ct)
    {
        if (hypotheses.Count == 0) return [];

        var docs = new List<SearchDoc>();
        var seen = new HashSet<string>();
        var failures = 0;
        foreach (var hypothesis in hypotheses)
        {
            try
            {
                foreach (var doc in await SearchAsync(hypothesis, ct))
                    if (seen.Add(doc.WorkId)) docs.Add(doc);
            }
            catch (CatalogUnavailableException ex)
            {
                failures++;
                log.LogWarning(ex, "Open Library search failed for one hypothesis");
            }
        }
        if (failures == hypotheses.Count)
            throw new CatalogUnavailableException("Open Library could not be searched.");

        using var gate = new SemaphoreSlim(EnrichmentConcurrency);
        var tasks = docs.Take(MaxEnriched).Select(async doc =>
        {
            await gate.WaitAsync(ct);
            try { return await EnrichAsync(doc, ct); }
            finally { gate.Release(); }
        });
        return await Task.WhenAll(tasks);
    }

    private async Task<IReadOnlyList<SearchDoc>> SearchAsync(ParsedQuery hypothesis, CancellationToken ct)
    {
        var hasTitle = !string.IsNullOrWhiteSpace(hypothesis.Title);
        var hasAuthor = !string.IsNullOrWhiteSpace(hypothesis.Author);
        if (!hasTitle && !hasAuthor) return [];

        var parts = new List<string> { $"fields={SearchFields}", $"limit={DocsPerSearch}" };
        if (hasTitle) parts.Add("title=" + Uri.EscapeDataString(hypothesis.Title!));
        if (hasAuthor) parts.Add("author=" + Uri.EscapeDataString(hypothesis.Author!));
        if (!hasTitle) parts.Add("sort=editions");

        var docs = ParseDocs(await GetJsonAsync("search.json?" + string.Join('&', parts), ct));
        if (docs.Count == 0 && hasTitle && hasAuthor)
        {
            var free = Uri.EscapeDataString($"{hypothesis.Title} {hypothesis.Author}");
            docs = ParseDocs(await GetJsonAsync($"search.json?fields={SearchFields}&limit={DocsPerSearch}&q={free}", ct));
        }
        return docs;
    }

    private static List<SearchDoc> ParseDocs(JsonNode? json)
    {
        var result = new List<SearchDoc>();
        if (json?["docs"] is not JsonArray array) return result;

        foreach (var node in array)
        {
            var key = node?["key"]?.GetValue<string>();
            var title = node?["title"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(title)) continue;

            var names = node!["author_name"] is JsonArray authorNames
                ? authorNames.Select(n => n?.GetValue<string>()).OfType<string>().ToList()
                : [];
            result.Add(new SearchDoc(
                key.Split('/')[^1],
                title,
                names,
                node["first_publish_year"]?.GetValue<int>(),
                node["edition_count"]?.GetValue<int>() ?? 0,
                node["cover_i"]?.GetValue<int>()));
        }
        return result;
    }

    private async Task<CatalogCandidate> EnrichAsync(SearchDoc doc, CancellationToken ct)
    {
        try
        {
            var work = await GetJsonAsync($"works/{doc.WorkId}.json", ct);
            var authorIds = (work?["authors"] as JsonArray ?? [])
                .Select(a => a?["author"]?["key"]?.GetValue<string>())
                .OfType<string>()
                .Select(k => k.Split('/')[^1])
                .Distinct()
                .ToList();

            var names = await Task.WhenAll(authorIds.Select(id => AuthorNameAsync(id, ct)));
            var primary = names.OfType<string>().Distinct().ToList();
            if (primary.Count == 0) return Unenriched(doc);

            var contributors = doc.AuthorNames
                .Where(n => !primary.Any(p => Normalizer.NormalizeName(p) == Normalizer.NormalizeName(n)))
                .ToList();
            return new CatalogCandidate(doc.WorkId, doc.Title, primary, contributors,
                doc.FirstPublishYear, doc.EditionCount, doc.CoverId, true);
        }
        catch (CatalogUnavailableException ex)
        {
            log.LogWarning(ex, "Could not enrich work {WorkId}", doc.WorkId);
            return Unenriched(doc);
        }
    }

    private static CatalogCandidate Unenriched(SearchDoc doc)
        => new(doc.WorkId, doc.Title, [], doc.AuthorNames, doc.FirstPublishYear, doc.EditionCount, doc.CoverId, false);

    private async Task<string?> AuthorNameAsync(string authorId, CancellationToken ct)
    {
        var json = await GetJsonAsync($"authors/{authorId}.json", ct);
        return json?["name"]?.GetValue<string>();
    }

    private async Task<JsonNode?> GetJsonAsync(string relativeUrl, CancellationToken ct)
    {
        var json = await cache.GetOrCreateAsync(relativeUrl, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheLifetime;
            return await FetchAsync(relativeUrl, ct);
        });
        return json;
    }

    private async Task<JsonNode?> FetchAsync(string relativeUrl, CancellationToken ct)
    {
        var settings = options.Value;
        for (var attempt = 0; ; attempt++)
        {
            string failure;
            try
            {
                using var response = await http.GetAsync(relativeUrl, ct);
                if (response.IsSuccessStatusCode) return await response.Content.ReadFromJsonAsync<JsonNode>(ct);
                if (response.StatusCode == HttpStatusCode.NotFound) return null;

                var retryable = (int)response.StatusCode >= 500 || response.StatusCode == HttpStatusCode.TooManyRequests;
                if (!retryable) throw new CatalogUnavailableException($"Open Library returned {(int)response.StatusCode}.");
                failure = $"Open Library returned {(int)response.StatusCode}.";
            }
            catch (HttpRequestException ex)
            {
                failure = ex.Message;
            }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested)
            {
                failure = "Open Library timed out.";
            }
            catch (JsonException ex)
            {
                failure = "Open Library returned invalid JSON: " + ex.Message;
            }

            if (attempt >= settings.MaxRetries) throw new CatalogUnavailableException(failure);
            await Task.Delay(settings.RetryDelayMs * (attempt + 1), ct);
        }
    }
}
