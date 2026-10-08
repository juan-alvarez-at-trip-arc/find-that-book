using System.Net;
using FindThatBook.Application.Abstractions;
using FindThatBook.Application.Models;
using FindThatBook.Infrastructure.OpenLibrary;
using FindThatBook.Tests.Support;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FindThatBook.Tests;

public class OpenLibraryCatalogTests
{
    private const string HobbitSearch = """
        {"docs":[{"key":"/works/OL1W","title":"The Hobbit","author_name":["J.R.R. Tolkien","Dixon"],
        "author_key":["OL1A","OL2A"],"first_publish_year":1937,"edition_count":120,"cover_i":99}]}
        """;

    private static OpenLibraryCatalog Catalog(StubHandler handler)
        => new(
            new HttpClient(handler) { BaseAddress = new Uri("https://openlibrary.org/") },
            new MemoryCache(new MemoryCacheOptions()),
            NullLogger<OpenLibraryCatalog>.Instance,
            Options.Create(new OpenLibraryOptions { RetryDelayMs = 0 }));

    private static HttpResponseMessage Route(HttpRequestMessage req, Func<HttpRequestMessage, HttpResponseMessage>? search = null)
        => req.RequestUri!.AbsolutePath switch
        {
            "/search.json" => search?.Invoke(req) ?? StubHandler.Json(HobbitSearch),
            "/works/OL1W.json" => StubHandler.Json("""{"authors":[{"author":{"key":"/authors/OL1A"}}]}"""),
            "/authors/OL1A.json" => StubHandler.Json("""{"name":"J.R.R. Tolkien"}"""),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        };

    [Fact]
    public async Task Resolves_primary_authors_from_the_work_and_treats_the_rest_as_contributors()
    {
        var handler = new StubHandler(req => Route(req));

        var found = await Catalog(handler).FindAsync([Q.Make("the hobbit", "tolkien")], default);

        var book = Assert.Single(found);
        Assert.Equal("OL1W", book.WorkId);
        Assert.Equal(["J.R.R. Tolkien"], book.PrimaryAuthors);
        Assert.Equal(["Dixon"], book.Contributors);
        Assert.Equal(1937, book.FirstPublishYear);
        Assert.Equal(120, book.EditionCount);
        Assert.Equal(99, book.CoverId);
        Assert.True(book.Enriched);
    }

    [Fact]
    public async Task Author_only_search_sorts_by_editions()
    {
        var handler = new StubHandler(req => Route(req));

        await Catalog(handler).FindAsync([Q.Make(null, "tolkien")], default);

        Assert.Contains(handler.Requests, r => r.StartsWith("/search.json") && r.Contains("author=tolkien") && r.Contains("sort=editions"));
    }

    [Fact]
    public async Task Failed_work_lookup_keeps_the_candidate_as_unenriched()
    {
        var handler = new StubHandler(req => req.RequestUri!.AbsolutePath == "/works/OL1W.json"
            ? new HttpResponseMessage(HttpStatusCode.InternalServerError)
            : Route(req));

        var found = await Catalog(handler).FindAsync([Q.Make("the hobbit", "tolkien")], default);

        var book = Assert.Single(found);
        Assert.False(book.Enriched);
        Assert.Empty(book.PrimaryAuthors);
        Assert.Equal(["J.R.R. Tolkien", "Dixon"], book.Contributors);
    }

    [Fact]
    public async Task Missing_work_record_404_keeps_the_candidate_as_unenriched()
    {
        var handler = new StubHandler(req => req.RequestUri!.AbsolutePath == "/works/OL1W.json"
            ? new HttpResponseMessage(HttpStatusCode.NotFound)
            : Route(req));

        var book = Assert.Single(await Catalog(handler).FindAsync([Q.Make("the hobbit", "tolkien")], default));

        Assert.False(book.Enriched);
    }

    [Fact]
    public async Task Docs_missing_optional_fields_do_not_crash()
    {
        var handler = new StubHandler(req => Route(req, _ => StubHandler.Json("""{"docs":[{"key":"/works/OL9W","title":"Mystery"}]}""")));

        var book = Assert.Single(await Catalog(handler).FindAsync([Q.Make("mystery", null)], default));

        Assert.Equal("OL9W", book.WorkId);
        Assert.Null(book.FirstPublishYear);
        Assert.Null(book.CoverId);
        Assert.Equal(0, book.EditionCount);
        Assert.Empty(book.Contributors);
    }

    [Fact]
    public async Task Search_that_always_fails_throws_after_retries()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        await Assert.ThrowsAsync<CatalogUnavailableException>(() => Catalog(handler).FindAsync([Q.Make("hobbit", null)], default));

        Assert.Equal(3, handler.Requests.Count);
    }

    [Fact]
    public async Task One_failing_hypothesis_does_not_fail_the_request()
    {
        var handler = new StubHandler(req => req.RequestUri!.Query.Contains("title=bad")
            ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            : Route(req));

        var found = await Catalog(handler).FindAsync([Q.Make("bad", null), Q.Make("the hobbit", null)], default);

        Assert.Single(found);
    }

    [Fact]
    public async Task Empty_hypothesis_list_makes_no_requests()
    {
        var handler = new StubHandler(_ => throw new InvalidOperationException("no request expected"));

        var found = await Catalog(handler).FindAsync([], default);

        Assert.Empty(found);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Fielded_search_with_no_hits_retries_with_free_text()
    {
        var handler = new StubHandler(req => Route(req, r => r.RequestUri!.Query.Contains("q=")
            ? StubHandler.Json(HobbitSearch)
            : StubHandler.Json("""{"docs":[]}""")));

        var found = await Catalog(handler).FindAsync([Q.Make("hobit", "tolkein")], default);

        Assert.Single(found);
        Assert.Contains(handler.Requests, r => r.Contains("q=hobit%20tolkein"));
    }

    [Fact]
    public async Task Same_work_from_two_hypotheses_is_returned_once()
    {
        var handler = new StubHandler(req => Route(req));

        var found = await Catalog(handler).FindAsync([Q.Make("the hobbit", null), Q.Make(null, "tolkien")], default);

        Assert.Single(found);
    }
}
