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
    IOptions<OpenLibraryOptions> options,
    OpenLibraryGate? limiter = null) : IBookCatalog
{
    private const string SearchFields = "key,title,author_name,author_key,first_publish_year,edition_count,cover_i,editions";
    private const int DocsPerSearch = 5;
    private const int EnrichmentConcurrency = 4;
    private readonly OpenLibraryGate _limiter = limiter ?? new OpenLibraryGate(options.Value.MaxConcurrentRequests);
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromHours(1);

    private sealed record SearchDoc(
        string WorkId, string Title, IReadOnlyList<string> AuthorNames,
        int? FirstPublishYear, int EditionCount, int? CoverId, IReadOnlyList<string> EditionTitles);

    public async Task<IReadOnlyList<CatalogCandidate>> FindAsync(IReadOnlyList<ParsedQuery> hypotheses, CancellationToken ct)
    {
        if (hypotheses.Count == 0) return [];

        var searches = await Task.WhenAll(hypotheses.Select(h => SearchSafelyAsync(h, ct)));
        if (searches.All(s => s is null))
            throw new CatalogUnavailableException("Open Library could not be searched.");

        // Round-robin across hypotheses so a late hypothesis is not starved by the enrichment cap.
        var lists = searches.OfType<IReadOnlyList<SearchDoc>>().ToList();
        var maxDocs = Math.Max(1, options.Value.MaxDetailLookups);
        var docs = new List<SearchDoc>();
        var seen = new HashSet<string>();
        for (var i = 0; docs.Count < maxDocs && lists.Any(l => i < l.Count); i++)
        {
            foreach (var list in lists)
            {
                if (i < list.Count && docs.Count < maxDocs && seen.Add(list[i].WorkId)) docs.Add(list[i]);
            }
        }

        using var gate = new SemaphoreSlim(EnrichmentConcurrency);
        var tasks = docs.Select(async doc =>
        {
            await gate.WaitAsync(ct);
            try { return await EnrichAsync(doc, ct); }
            finally { gate.Release(); }
        });
        return await Task.WhenAll(tasks);
    }

    private async Task<IReadOnlyList<SearchDoc>?> SearchSafelyAsync(ParsedQuery hypothesis, CancellationToken ct)
    {
        try
        {
            return await SearchAsync(hypothesis, ct);
        }
        catch (CatalogUnavailableException ex)
        {
            log.LogWarning(ex, "Open Library search failed for one hypothesis");
            return null;
        }
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
            try
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
                    node["cover_i"]?.GetValue<int>(),
                    EditionTitles(node, title)));
            }
            catch (InvalidOperationException)
            {
                // A doc with a wrongly typed field is skipped; the rest of the page is still usable.
            }
        }
        return result;
    }

    /// <summary>Titles of the editions Open Library matched for this work, other than the work's own title.</summary>
    private static List<string> EditionTitles(JsonNode doc, string workTitle)
    {
        if (doc["editions"]?["docs"] is not JsonArray editions) return [];

        return editions
            .Select(e => e?["title"] is JsonValue v && v.TryGetValue<string>(out var t) ? t : null)
            .OfType<string>()
            .Where(t => !string.IsNullOrWhiteSpace(t) && !string.Equals(t, workTitle, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
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
                .Take(Math.Max(1, options.Value.MaxAuthorsPerWork))
                .ToList();

            var authors = (await Task.WhenAll(authorIds.Select(id => AuthorAsync(id, ct)))).OfType<AuthorInfo>().ToList();
            var primary = authors.Select(a => a.Name).Distinct().ToList();
            if (primary.Count == 0) return Unenriched(doc);

            var contributors = doc.AuthorNames
                .Where(n => !primary.Any(p => Normalizer.NormalizeName(p) == Normalizer.NormalizeName(n)))
                .ToList();
            var aliases = authors.SelectMany(a => a.Aliases).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            return new CatalogCandidate(doc.WorkId, doc.Title, primary, contributors,
                doc.FirstPublishYear, doc.EditionCount, doc.CoverId, true, doc.EditionTitles, aliases);
        }
        catch (CatalogUnavailableException ex)
        {
            log.LogWarning(ex, "Could not enrich work {WorkId}", doc.WorkId);
            return Unenriched(doc);
        }
    }

    private static CatalogCandidate Unenriched(SearchDoc doc)
        => new(doc.WorkId, doc.Title, [], doc.AuthorNames, doc.FirstPublishYear, doc.EditionCount, doc.CoverId, false, doc.EditionTitles, []);

    private const int MaxAliasesPerAuthor = 40;

    /// <param name="Name">The display name, as Open Library shows it (can be in another script).</param>
    /// <param name="Aliases">Other spellings used only for matching: the personal name and alternate names.</param>
    private sealed record AuthorInfo(string Name, IReadOnlyList<string> Aliases);

    private async Task<AuthorInfo?> AuthorAsync(string authorId, CancellationToken ct)
    {
        var json = await GetJsonAsync($"authors/{authorId}.json", ct);
        var name = json?["name"] is JsonValue n && n.TryGetValue<string>(out var s) && !string.IsNullOrWhiteSpace(s) ? s : null;
        if (name is null) return null;

        var aliases = new List<string>();
        if (json!["personal_name"] is JsonValue personal && personal.TryGetValue<string>(out var p)) aliases.Add(p);
        if (json["alternate_names"] is JsonArray alternates)
        {
            aliases.AddRange(alternates
                .Select(a => a is JsonValue v && v.TryGetValue<string>(out var t) ? t : null)
                .OfType<string>());
        }

        return new AuthorInfo(name, aliases
            .Where(a => !string.IsNullOrWhiteSpace(a) && !string.Equals(a, name, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxAliasesPerAuthor)
            .ToList());
    }

    private async Task<JsonNode?> GetJsonAsync(string relativeUrl, CancellationToken ct)
    {
        var json = await cache.GetOrCreateAsync(relativeUrl, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheLifetime;
            entry.Size = 1;
            return await FetchAsync(relativeUrl, ct);
        });
        return json;
    }

    private async Task<JsonNode?> FetchAsync(string relativeUrl, CancellationToken ct)
    {
        var settings = options.Value;
        for (var attempt = 0; ; attempt++)
        {
            // While Open Library is asking us to slow down, do not send anything at all.
            if (_limiter.IsCoolingDown(out var remaining))
                throw new CatalogUnavailableException($"Open Library asked us to slow down; retry in {Math.Ceiling(remaining.TotalSeconds)}s.");

            string failure;
            try
            {
                using var slot = await _limiter.WaitAsync(ct);
                using var response = await http.GetAsync(relativeUrl, ct);
                if (response.IsSuccessStatusCode) return await response.Content.ReadFromJsonAsync<JsonNode>(ct);
                if (response.StatusCode == HttpStatusCode.NotFound) return null;

                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    // Retrying a 429 only adds load: pause every request for what Open Library asked, and fail this one.
                    _limiter.StartCooldown(response.Headers.RetryAfter, TimeSpan.FromSeconds(Math.Max(1, settings.DefaultCooldownSeconds)));
                    throw new CatalogUnavailableException("Open Library is rate limiting requests.");
                }

                var retryable = (int)response.StatusCode >= 500;
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
