using System.Net;
using System.Net.Http.Headers;
using FindThatBook.Application.Abstractions;
using FindThatBook.Infrastructure.OpenLibrary;
using FindThatBook.Tests.Support;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FindThatBook.Tests;

public class OpenLibraryLoadTests
{
    private static string Docs(int count) => "{\"docs\":[" + string.Join(",",
        Enumerable.Range(1, count).Select(i => $"{{\"key\":\"/works/OL{i}W\",\"title\":\"Book {i}\",\"author_name\":[\"Author\"]}}"))
        + "]}";

    private static OpenLibraryCatalog Catalog(HttpMessageHandler handler, OpenLibraryOptions? options = null, OpenLibraryGate? gate = null, IMemoryCache? cache = null)
    {
        options ??= new OpenLibraryOptions();
        options.RetryDelayMs = 0;
        return new OpenLibraryCatalog(
            new HttpClient(handler) { BaseAddress = new Uri("https://openlibrary.org/") },
            cache ?? new MemoryCache(new MemoryCacheOptions()),
            NullLogger<OpenLibraryCatalog>.Instance,
            Options.Create(options),
            gate);
    }

    private static int Count(StubHandler handler, string prefix) => handler.Requests.Count(r => r.StartsWith(prefix));

    [Fact]
    public async Task Only_the_configured_number_of_works_get_detail_lookups()
    {
        var handler = new StubHandler(req => req.RequestUri!.AbsolutePath == "/search.json"
            ? StubHandler.Json(Docs(5))
            : new HttpResponseMessage(HttpStatusCode.NotFound));

        var found = await Catalog(handler, new OpenLibraryOptions { MaxDetailLookups = 2 }).FindAsync([Q.Make("book", null)], default);

        Assert.Equal(2, found.Count);
        Assert.Equal(2, Count(handler, "/works/"));
    }

    [Fact]
    public async Task Detail_lookups_default_to_six_works_per_search()
    {
        var handler = new StubHandler(req => req.RequestUri!.AbsolutePath == "/search.json"
            ? StubHandler.Json(Docs(5))
            : new HttpResponseMessage(HttpStatusCode.NotFound));
        var hypotheses = new[] { Q.Make("a", null), Q.Make("b", null) };

        await Catalog(handler).FindAsync(hypotheses, default);

        Assert.True(Count(handler, "/works/") <= 6, $"looked up {Count(handler, "/works/")} works");
    }

    [Fact]
    public async Task Author_lookups_are_capped_per_work()
    {
        var handler = new StubHandler(req => req.RequestUri!.AbsolutePath switch
        {
            "/search.json" => StubHandler.Json(Docs(1)),
            "/works/OL1W.json" => StubHandler.Json("{\"authors\":[" + string.Join(",",
                Enumerable.Range(1, 6).Select(i => $"{{\"author\":{{\"key\":\"/authors/OL{i}A\"}}}}")) + "]}"),
            var p when p.StartsWith("/authors/") => StubHandler.Json($"{{\"name\":\"Name {p}\"}}"),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        });

        var found = await Catalog(handler).FindAsync([Q.Make("book", null)], default);

        Assert.Equal(3, Count(handler, "/authors/"));
        Assert.Equal(3, Assert.Single(found).PrimaryAuthors.Count);
    }

    [Fact]
    public async Task An_author_shared_by_several_works_is_fetched_once_per_search()
    {
        var authorRequests = 0;
        var handler = new AsyncStubHandler(async (req, ct) =>
        {
            await Task.Delay(40, ct);
            var path = req.RequestUri!.AbsolutePath;
            if (path == "/authors/OL1A.json") Interlocked.Increment(ref authorRequests);
            if (path == "/search.json") return StubHandler.Json(Docs(3));
            if (path.StartsWith("/works/")) return StubHandler.Json("{\"authors\":[{\"author\":{\"key\":\"/authors/OL1A\"}}]}");
            return StubHandler.Json("{\"name\":\"Shared Author\"}");
        });

        var found = await Catalog(handler).FindAsync([Q.Make("book", null)], default);

        Assert.Equal(3, found.Count);
        Assert.Equal(1, authorRequests);
    }

    [Fact]
    public async Task Requests_never_run_more_than_the_configured_number_at_once()
    {
        var inFlight = 0;
        var peak = 0;
        var handler = new AsyncStubHandler(async (req, ct) =>
        {
            var now = Interlocked.Increment(ref inFlight);
            int seen;
            while (now > (seen = Volatile.Read(ref peak))) Interlocked.CompareExchange(ref peak, now, seen);
            try
            {
                await Task.Delay(40, ct);
                var path = req.RequestUri!.AbsolutePath;
                if (path == "/search.json") return StubHandler.Json(Docs(5));
                if (path.StartsWith("/works/"))
                {
                    var id = path.Split('/')[2].Replace(".json", "");
                    return StubHandler.Json($"{{\"authors\":[{{\"author\":{{\"key\":\"/authors/{id}A\"}}}},{{\"author\":{{\"key\":\"/authors/{id}B\"}}}}]}}");
                }
                return StubHandler.Json("{\"name\":\"Someone\"}");
            }
            finally { Interlocked.Decrement(ref inFlight); }
        });

        await Catalog(handler, new OpenLibraryOptions { MaxConcurrentRequests = 2 }).FindAsync([Q.Make("book", null)], default);

        Assert.InRange(peak, 1, 2);
    }

    private static HttpResponseMessage TooMany(int? retryAfterSeconds)
    {
        var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        if (retryAfterSeconds is { } s) response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(s));
        return response;
    }

    [Fact]
    public async Task A_429_is_not_retried_and_starts_a_cooldown_that_fails_later_calls_without_any_request()
    {
        var clock = new FakeClock(DateTimeOffset.UnixEpoch);
        var handler = new StubHandler(_ => TooMany(5));
        var catalog = Catalog(handler, gate: new OpenLibraryGate(2, clock));

        await Assert.ThrowsAsync<CatalogUnavailableException>(() => catalog.FindAsync([Q.Make("book", null)], default));
        Assert.Single(handler.Requests);

        await Assert.ThrowsAsync<CatalogUnavailableException>(() => catalog.FindAsync([Q.Make("other", null)], default));
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task The_cooldown_follows_retry_after_and_then_requests_resume()
    {
        var clock = new FakeClock(DateTimeOffset.UnixEpoch);
        var throttled = true;
        var handler = new StubHandler(_ => throttled ? TooMany(5) : StubHandler.Json(Docs(1)));
        var catalog = Catalog(handler, gate: new OpenLibraryGate(2, clock));
        await Assert.ThrowsAsync<CatalogUnavailableException>(() => catalog.FindAsync([Q.Make("book", null)], default));

        throttled = false;
        clock.Advance(TimeSpan.FromSeconds(4));
        await Assert.ThrowsAsync<CatalogUnavailableException>(() => catalog.FindAsync([Q.Make("book two", null)], default));
        Assert.Single(handler.Requests);

        clock.Advance(TimeSpan.FromSeconds(2));
        var found = await catalog.FindAsync([Q.Make("book three", null)], default);

        Assert.NotEmpty(found);
    }

    [Fact]
    public async Task Without_retry_after_the_cooldown_defaults_to_thirty_seconds()
    {
        var clock = new FakeClock(DateTimeOffset.UnixEpoch);
        var throttled = true;
        var handler = new StubHandler(_ => throttled ? TooMany(null) : StubHandler.Json(Docs(1)));
        var catalog = Catalog(handler, gate: new OpenLibraryGate(2, clock));
        await Assert.ThrowsAsync<CatalogUnavailableException>(() => catalog.FindAsync([Q.Make("book", null)], default));

        throttled = false;
        clock.Advance(TimeSpan.FromSeconds(29));
        await Assert.ThrowsAsync<CatalogUnavailableException>(() => catalog.FindAsync([Q.Make("book two", null)], default));

        clock.Advance(TimeSpan.FromSeconds(2));
        Assert.NotEmpty(await catalog.FindAsync([Q.Make("book three", null)], default));
    }

    [Fact]
    public async Task Cached_lookups_still_work_during_a_cooldown()
    {
        var clock = new FakeClock(DateTimeOffset.UnixEpoch);
        var gate = new OpenLibraryGate(2, clock);
        var handler = new StubHandler(_ => StubHandler.Json(Docs(1)));
        var catalog = Catalog(handler, gate: gate);
        Assert.NotEmpty(await catalog.FindAsync([Q.Make("book", null)], default));
        var requestsBefore = handler.Requests.Count;

        gate.StartCooldown(null, TimeSpan.FromSeconds(60));
        var again = await catalog.FindAsync([Q.Make("book", null)], default);

        Assert.NotEmpty(again);
        Assert.Equal(requestsBefore, handler.Requests.Count);
    }

    [Fact]
    public async Task Server_errors_are_still_retried()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        await Assert.ThrowsAsync<CatalogUnavailableException>(() => Catalog(handler).FindAsync([Q.Make("book", null)], default));

        Assert.Equal(3, handler.Requests.Count);
    }

    [Fact]
    public void User_agent_names_the_app_and_only_includes_a_contact_when_one_is_configured()
    {
        Assert.StartsWith("FindThatBook/", new OpenLibraryOptions().UserAgent);
        Assert.DoesNotContain("@", new OpenLibraryOptions().UserAgent);

        var withContact = new OpenLibraryOptions { Contact = "ops@example.org" }.UserAgent;
        Assert.Contains("ops@example.org", withContact);

        Assert.DoesNotContain("example", new OpenLibraryOptions { Contact = "  " }.UserAgent);
    }
}
