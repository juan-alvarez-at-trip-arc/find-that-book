using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using FindThatBook.Application.Abstractions;
using FindThatBook.Application.Models;
using FindThatBook.Application.Telemetry;
using FindThatBook.Application.Text;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace FindThatBook.Infrastructure.OpenLibrary;

/// <summary>
/// Book catalog backed by the Open Library search, works and authors APIs. It runs one search per hypothesis, looks up details for a capped number of works, and returns candidates carrying edition titles and author aliases for matching.
/// </summary>
/// <param name="http">Client whose base address points at Open Library.</param>
/// <param name="cache">Response cache so repeated lookups do not hit Open Library again.</param>
/// <param name="log">Logger for failed searches and detail lookups.</param>
/// <param name="options">Limits for detail lookups, retries and cooldown.</param>
/// <param name="limiter">Shared request gate with the concurrency cap and 429 cooldown. When null, a private gate is created from <paramref name="options"/>.</param>
/// <param name="resilience">Shared retry, circuit breaker and timeout pipeline. When null, a private one is created from <paramref name="options"/>.</param>
public sealed class OpenLibraryCatalog(
    HttpClient http,
    IMemoryCache cache,
    ILogger<OpenLibraryCatalog> log,
    IOptions<OpenLibraryOptions> options,
    OpenLibraryGate? limiter = null,
    OpenLibraryResiliencePipeline? resilience = null) : IBookCatalog
{
    private const string SearchFields = "key,title,author_name,author_key,first_publish_year,edition_count,cover_i,editions";
    private const int DocsPerSearch = 5;
    private const int MinFieldedHits = 3;
    private const int EnrichmentConcurrency = 4;
    private readonly OpenLibraryGate _limiter = limiter ?? new OpenLibraryGate(options.Value.MaxConcurrentRequests);
    private readonly OpenLibraryResiliencePipeline _resilience = resilience ?? OpenLibraryResiliencePipeline.Create(options.Value.Resilience);
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromHours(1);
    private readonly ConcurrentDictionary<string, Lazy<Task<JsonNode?>>> _inFlight = new();
    private static readonly Regex WorkKey = new(@"^/works/(OL\d+W)$", RegexOptions.Compiled);
    private static readonly Regex AuthorKey = new(@"^/authors/(OL\d+A)$", RegexOptions.Compiled);

    /// <summary>One work as returned by an Open Library search, before its detail lookup.</summary>
    /// <param name="WorkId">Open Library work id without the path prefix.</param>
    /// <param name="Title">The work's title.</param>
    /// <param name="AuthorNames">Author names listed on the search result.</param>
    /// <param name="FirstPublishYear">Year of first publication, when known.</param>
    /// <param name="EditionCount">Number of editions Open Library holds for the work.</param>
    /// <param name="CoverId">Open Library cover id, when the work has a cover.</param>
    /// <param name="EditionTitles">Titles of matched editions that differ from the work title, used for title matching.</param>
    private sealed record SearchDoc(
        string WorkId, string Title, IReadOnlyList<string> AuthorNames,
        int? FirstPublishYear, int EditionCount, int? CoverId, IReadOnlyList<string> EditionTitles);

    /// <summary>
    /// Searches every hypothesis, merges the results round-robin so no hypothesis is starved, and enriches up to the configured number of works with detail lookups. Works whose lookup fails are returned unenriched.
    /// </summary>
    /// <param name="hypotheses">Parsed interpretations of the user's query.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>De-duplicated candidates, one per work, enriched with primary authors and aliases where the lookup succeeded.</returns>
    /// <exception cref="CatalogUnavailableException">Every hypothesis search failed.</exception>
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

    /// <summary>Runs one hypothesis search and turns an Open Library failure into a logged null, so one failing hypothesis does not fail the whole search.</summary>
    /// <param name="hypothesis">The interpretation to search for.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The found works, or null when Open Library could not be searched for this hypothesis.</returns>
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

    /// <summary>Searches Open Library by title and author. When the field search finds fewer than three works, it also runs a free-text query and appends any works the field search missed, because the field search can miss a work whose title is in another language.</summary>
    /// <param name="hypothesis">The interpretation to search for.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The field search results followed by new free-text results, or an empty list when the hypothesis has no title and no author. The field results are kept when only the free-text query fails.</returns>
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
        if (docs.Count >= MinFieldedHits) return docs;

        var free = Uri.EscapeDataString($"{hypothesis.Title} {hypothesis.Author}".Trim());
        try
        {
            var extra = ParseDocs(await GetJsonAsync($"search.json?fields={SearchFields}&limit={DocsPerSearch}&q={free}", ct));
            var seen = docs.Select(d => d.WorkId).ToHashSet();
            return docs.Concat(extra.Where(d => seen.Add(d.WorkId))).ToList();
        }
        catch (CatalogUnavailableException) when (docs.Count > 0)
        {
            return docs;
        }
    }

    /// <summary>Reads works from a search response, skipping entries without a key or title and entries with wrongly typed fields.</summary>
    /// <param name="json">The search response body, or null when nothing was returned.</param>
    /// <returns>The parsed works; empty when the response has no docs.</returns>
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
                if (string.IsNullOrWhiteSpace(title) || key is null || WorkKey.Match(key) is not { Success: true } workKey) continue;

                var names = node!["author_name"] is JsonArray authorNames
                    ? authorNames.Select(n => n?.GetValue<string>()).OfType<string>().ToList()
                    : [];
                result.Add(new SearchDoc(
                    workKey.Groups[1].Value,
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
    /// <param name="doc">The search result entry for the work.</param>
    /// <param name="workTitle">The work's own title, which is excluded.</param>
    /// <returns>Distinct edition titles, compared case-insensitively.</returns>
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

    /// <summary>Looks up the work and its authors to separate primary authors from contributors and to collect author aliases for matching. A failed lookup yields an unenriched candidate instead of an error.</summary>
    /// <param name="doc">The search result to enrich.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An enriched candidate, or an unenriched one when the lookup failed or no author name was found.</returns>
    private async Task<CatalogCandidate> EnrichAsync(SearchDoc doc, CancellationToken ct)
    {
        try
        {
            var work = await GetJsonAsync($"works/{doc.WorkId}.json", ct);
            var authorIds = (work?["authors"] as JsonArray ?? [])
                .Select(a => a?["author"]?["key"]?.GetValue<string>())
                .OfType<string>()
                .Select(k => AuthorKey.Match(k))
                .Where(m => m.Success)
                .Select(m => m.Groups[1].Value)
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

    /// <summary>Builds a candidate from search data alone, with no primary authors or aliases, so the work can still be ranked when its detail lookup fails.</summary>
    /// <param name="doc">The search result to convert.</param>
    /// <returns>A candidate marked as not enriched.</returns>
    private static CatalogCandidate Unenriched(SearchDoc doc)
        => new(doc.WorkId, doc.Title, [], doc.AuthorNames, doc.FirstPublishYear, doc.EditionCount, doc.CoverId, false, doc.EditionTitles, []);

    private const int MaxAliasesPerAuthor = 40;

    /// <summary>An author from the Open Library authors API: the display name plus alternate spellings.</summary>
    /// <param name="Name">The display name, as Open Library shows it (can be in another script).</param>
    /// <param name="Aliases">Other spellings used only for matching: the personal name and alternate names.</param>
    private sealed record AuthorInfo(string Name, IReadOnlyList<string> Aliases);

    /// <summary>Fetches an author's display name and aliases (personal name and alternate names, capped, excluding the display name).</summary>
    /// <param name="authorId">Open Library author id without the path prefix.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The author, or null when the author is not found or has no name.</returns>
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

    /// <summary>Returns the JSON for a relative Open Library URL, served from the cache for an hour so repeated lookups cost no requests. Concurrent calls for the same URL share one request.</summary>
    /// <param name="relativeUrl">Path and query relative to the Open Library base address, also used as the cache key.</param>
    /// <param name="ct">Cancellation token of this caller. It stops this caller from waiting, but never cancels a request other callers share.</param>
    /// <returns>The parsed JSON, or null when Open Library answered 404.</returns>
    private async Task<JsonNode?> GetJsonAsync(string relativeUrl, CancellationToken ct)
    {
        var pending = _inFlight.GetOrAdd(relativeUrl, url => new Lazy<Task<JsonNode?>>(() => FetchSharedAsync(url)));
        return await pending.Value.WaitAsync(ct);
    }

    /// <summary>Runs the request shared by every caller of one URL, on its own lifetime so that no caller's cancellation can fail the others. It is bounded by the HTTP timeout and the retry limit. The in-flight entry is removed when the request ends.</summary>
    /// <param name="relativeUrl">Path and query relative to the Open Library base address.</param>
    /// <returns>The parsed JSON, or null when Open Library answered 404.</returns>
    private async Task<JsonNode?> FetchSharedAsync(string relativeUrl)
    {
        try
        {
            return await CachedFetchAsync(relativeUrl, CancellationToken.None);
        }
        finally
        {
            _inFlight.TryRemove(relativeUrl, out _);
        }
    }

    /// <summary>Reads the JSON for a relative URL from the cache, fetching and caching it on a miss.</summary>
    /// <param name="relativeUrl">Path and query relative to the Open Library base address, also used as the cache key.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The parsed JSON, or null when Open Library answered 404.</returns>
    private async Task<JsonNode?> CachedFetchAsync(string relativeUrl, CancellationToken ct)
    {
        var json = await cache.GetOrCreateAsync(relativeUrl, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheLifetime;
            entry.Size = 1;
            return await FetchAsync(relativeUrl, ct);
        });
        return json;
    }


    /// <summary>
    /// Sends one GET through the resilience pipeline. Only 5xx responses, timeouts, network errors and invalid JSON are retried with a growing delay, and repeated failures open a circuit that rejects requests for a while. A 429 starts the shared cooldown and fails immediately, and other 4xx responses fail immediately.
    /// </summary>
    /// <param name="relativeUrl">Path and query relative to the Open Library base address.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The parsed JSON, or null when Open Library answered 404.</returns>
    /// <exception cref="CatalogUnavailableException">The gate is cooling down, the circuit is open, Open Library returned 429 or another non-retryable error, or retries were exhausted.</exception>
    private async Task<JsonNode?> FetchAsync(string relativeUrl, CancellationToken ct)
    {
        try
        {
            return await _resilience.Pipeline.ExecuteAsync(token => new ValueTask<JsonNode?>(SendOnceAsync(relativeUrl, token)), ct);
        }
        catch (BrokenCircuitException ex)
        {
            throw new CatalogUnavailableException("Open Library is temporarily unavailable after repeated failures.", ex);
        }
        catch (TimeoutRejectedException ex)
        {
            throw new CatalogUnavailableException("Open Library timed out.", ex);
        }
        catch (OpenLibraryTransientException ex)
        {
            throw new CatalogUnavailableException(ex.Message, ex);
        }
        catch (HttpRequestException ex)
        {
            throw new CatalogUnavailableException(ex.Message, ex);
        }
        catch (JsonException ex)
        {
            throw new CatalogUnavailableException("Open Library returned invalid JSON: " + ex.Message, ex);
        }
    }

    /// <summary>Makes one attempt: checks the cooldown, takes a request slot from the shared gate, sends the GET and reads the JSON.</summary>
    /// <param name="relativeUrl">Path and query relative to the Open Library base address.</param>
    /// <param name="ct">Cancellation token of this attempt, which the pipeline cancels when the attempt times out.</param>
    /// <returns>The parsed JSON, or null when Open Library answered 404.</returns>
    /// <exception cref="CatalogUnavailableException">The gate is cooling down, Open Library returned 429 or a 4xx other than 404.</exception>
    /// <exception cref="OpenLibraryTransientException">Open Library returned 5xx or the HTTP client timed out.</exception>
    private async Task<JsonNode?> SendOnceAsync(string relativeUrl, CancellationToken ct)
    {
        if (_limiter.IsCoolingDown(out var remaining))
            throw new CatalogUnavailableException($"Open Library asked us to slow down; retry in {Math.Ceiling(remaining.TotalSeconds)}s.");

        try
        {
            using var slot = await _limiter.WaitAsync(ct);
            using var response = await http.GetAsync(relativeUrl, ct);
            if (response.IsSuccessStatusCode) return await response.Content.ReadFromJsonAsync<JsonNode>(ct);
            if (response.StatusCode == HttpStatusCode.NotFound) return null;

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                FindThatBookTelemetry.RecordOpenLibraryCooldown();
                _limiter.StartCooldown(response.Headers.RetryAfter, TimeSpan.FromSeconds(Math.Max(1, options.Value.DefaultCooldownSeconds)));
                throw new CatalogUnavailableException("Open Library is rate limiting requests.");
            }

            if ((int)response.StatusCode >= 500) throw new OpenLibraryTransientException($"Open Library returned {(int)response.StatusCode}.");
            throw new CatalogUnavailableException($"Open Library returned {(int)response.StatusCode}.");
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new OpenLibraryTransientException("Open Library timed out: " + ex.Message);
        }
    }
}
