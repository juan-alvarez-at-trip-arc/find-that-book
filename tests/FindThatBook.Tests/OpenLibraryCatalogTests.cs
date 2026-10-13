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
    public async Task Doc_with_a_wrongly_typed_field_is_skipped_not_fatal()
    {
        var handler = new StubHandler(req => Route(req, _ => StubHandler.Json(
            """{"docs":[{"key":"/works/OL8W","title":"Bad","first_publish_year":"soon"},{"key":"/works/OL1W","title":"The Hobbit","author_name":["J.R.R. Tolkien"]}]}""")));

        var found = await Catalog(handler).FindAsync([Q.Make("the hobbit", null)], default);

        Assert.Equal("OL1W", Assert.Single(found).WorkId);
    }

    [Fact]
    public async Task Enrichment_cap_takes_docs_round_robin_across_hypotheses()
    {
        string Docs(string prefix) => "{\"docs\":[" + string.Join(",",
            Enumerable.Range(1, 5).Select(i => $"{{\"key\":\"/works/OL{(prefix[0] - 'A' + 1) * 100 + i}W\",\"title\":\"{prefix} {i}\"}}")) + "]}";
        var handler = new StubHandler(req => Route(req, r =>
            StubHandler.Json(Docs(r.RequestUri!.Query.Contains("title=aaa") ? "A" : r.RequestUri.Query.Contains("title=bbb") ? "B" : "C"))));

        var found = await Catalog(handler).FindAsync([Q.Make("aaa", null), Q.Make("bbb", null), Q.Make("ccc", null)], default);

        Assert.Equal(6, found.Count);
        Assert.Contains(found, b => b.WorkId == "OL101W");
        Assert.Contains(found, b => b.WorkId == "OL201W");
        Assert.Contains(found, b => b.WorkId == "OL301W");
        Assert.DoesNotContain(found, b => b.WorkId == "OL103W");
    }

    [Fact]
    public async Task Works_with_a_size_limited_cache()
    {
        var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 3 });
        var catalog = new OpenLibraryCatalog(
            new HttpClient(new StubHandler(req => Route(req))) { BaseAddress = new Uri("https://openlibrary.org/") },
            cache, NullLogger<OpenLibraryCatalog>.Instance,
            Options.Create(new OpenLibraryOptions { RetryDelayMs = 0 }));

        var found = await catalog.FindAsync([Q.Make("the hobbit", "tolkien")], default);

        Assert.Single(found);
        Assert.True(cache.Count <= 3);
    }

    [Fact]
    public async Task Search_asks_Open_Library_for_the_matching_edition_titles()
    {
        var handler = new StubHandler(req => Route(req));

        await Catalog(handler).FindAsync([Q.Make("the hobbit", "tolkien")], default);

        Assert.Contains(handler.Requests, r => r.StartsWith("/search.json") && r.Contains("editions"));
    }

    [Fact]
    public async Task Matching_edition_titles_are_kept_as_alternative_titles()
    {
        const string search = """
            {"docs":[{"key":"/works/OL1W","title":"The Hobbit","author_name":["J.R.R. Tolkien"],"edition_count":3,
            "editions":{"docs":[{"key":"/books/OL1M","title":"El hobbit"},{"key":"/books/OL2M","title":"The Hobbit"},{"title":42},{"key":"/books/OL3M"}]}}]}
            """;
        var handler = new StubHandler(req => Route(req, _ => StubHandler.Json(search)));

        var book = Assert.Single(await Catalog(handler).FindAsync([Q.Make("el hobbit", null)], default));

        Assert.Equal(["El hobbit"], book.AlternativeTitles);
    }

    [Fact]
    public async Task Author_aliases_are_collected_but_the_display_name_stays_the_authors_name()
    {
        var handler = new StubHandler(req => req.RequestUri!.AbsolutePath == "/authors/OL1A.json"
            ? StubHandler.Json("""{"name":"村上春樹","personal_name":"Murakami, Haruki","alternate_names":["Haruki Murakami","HARUKI MURAKAMI",7,"村上春樹"]}""")
            : Route(req));

        var book = Assert.Single(await Catalog(handler).FindAsync([Q.Make("the hobbit", "murakami")], default));

        Assert.Equal(["村上春樹"], book.PrimaryAuthors);
        Assert.Contains("Murakami, Haruki", book.PrimaryAuthorAliases);
        Assert.Contains("Haruki Murakami", book.PrimaryAuthorAliases);
        Assert.DoesNotContain("村上春樹", book.PrimaryAuthorAliases);
        Assert.Equal(book.PrimaryAuthorAliases.Count, book.PrimaryAuthorAliases.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Theory]
    [InlineData("""{"docs":[{"key":"/works/OL1W","title":"A","editions":null}]}""")]
    [InlineData("""{"docs":[{"key":"/works/OL1W","title":"A","editions":{"docs":"nope"}}]}""")]
    [InlineData("""{"docs":[{"key":"/works/OL1W","title":"A","editions":{}}]}""")]
    [InlineData("""{"docs":[{"key":"/works/OL1W","title":"A"}]}""")]
    public async Task Missing_or_odd_edition_data_is_ignored(string search)
    {
        var handler = new StubHandler(req => Route(req, _ => StubHandler.Json(search)));

        var book = Assert.Single(await Catalog(handler).FindAsync([Q.Make("a", null)], default));

        Assert.Empty(book.AlternativeTitles);
    }

    [Fact]
    public async Task Unenriched_candidates_still_carry_their_edition_titles()
    {
        const string search = """{"docs":[{"key":"/works/OL1W","title":"海辺のカフカ","editions":{"docs":[{"title":"Kafka on the Shore"}]}}]}""";
        var handler = new StubHandler(req => req.RequestUri!.AbsolutePath == "/search.json"
            ? StubHandler.Json(search)
            : new HttpResponseMessage(HttpStatusCode.InternalServerError));

        var book = Assert.Single(await Catalog(handler).FindAsync([Q.Make("kafka on the shore", null)], default));

        Assert.False(book.Enriched);
        Assert.Equal(["Kafka on the Shore"], book.AlternativeTitles);
    }

    private static string DocsJson(params (string Id, string Title)[] docs)
        => "{\"docs\":[" + string.Join(",", docs.Select(d => $"{{\"key\":\"/works/{d.Id}\",\"title\":\"{d.Title}\"}}")) + "]}";

    private static bool IsFreeText(HttpRequestMessage req) => req.RequestUri!.Query.Contains("&q=");

    [Fact]
    public async Task A_fielded_search_with_few_hits_is_supplemented_by_the_free_text_search()
    {
        var handler = new StubHandler(req => Route(req, r => IsFreeText(r)
            ? StubHandler.Json(DocsJson(("OL2W", "Män som hatar kvinnor"), ("OL3W", "The girl with the dragon tattoo")))
            : StubHandler.Json(DocsJson(("OL1W", "Man SOM Hatar Kvinnor (Korean Edition)")))));

        var found = await Catalog(handler).FindAsync([Q.Make("the girl with the dragon tattoo", "stieg larsson")], default);

        Assert.Equal(["OL1W", "OL2W", "OL3W"], found.Select(b => b.WorkId));
        Assert.Contains(handler.Requests, r => r.StartsWith("/search.json") && r.Contains("&q=the%20girl%20with%20the%20dragon%20tattoo%20stieg%20larsson"));
    }

    [Fact]
    public async Task A_fielded_search_with_enough_hits_does_not_run_the_free_text_search()
    {
        var handler = new StubHandler(req => Route(req, _ => StubHandler.Json(DocsJson(("OL1W", "A"), ("OL2W", "B"), ("OL3W", "C")))));

        var found = await Catalog(handler).FindAsync([Q.Make("book", null)], default);

        Assert.Equal(3, found.Count);
        Assert.DoesNotContain(handler.Requests, r => r.Contains("&q="));
    }

    [Fact]
    public async Task A_title_only_search_with_no_hits_also_falls_back_to_free_text()
    {
        var handler = new StubHandler(req => Route(req, r => IsFreeText(r)
            ? StubHandler.Json(DocsJson(("OL9W", "海辺のカフカ")))
            : StubHandler.Json("""{"docs":[]}""")));

        var found = await Catalog(handler).FindAsync([Q.Make("kafka on the shore", null)], default);

        Assert.Equal("OL9W", Assert.Single(found).WorkId);
        Assert.Contains(handler.Requests, r => r.Contains("&q=kafka%20on%20the%20shore"));
    }

    [Fact]
    public async Task An_author_only_search_with_few_hits_is_supplemented_by_free_text()
    {
        var handler = new StubHandler(req => Route(req, r => IsFreeText(r)
            ? StubHandler.Json(DocsJson(("OL2W", "Another Work")))
            : StubHandler.Json(DocsJson(("OL1W", "First Work")))));

        var found = await Catalog(handler).FindAsync([Q.Make(null, "tolkien")], default);

        Assert.Equal(["OL1W", "OL2W"], found.Select(b => b.WorkId));
        Assert.Contains(handler.Requests, r => r.Contains("&q=tolkien"));
    }

    [Fact]
    public async Task Works_found_by_both_searches_are_returned_once()
    {
        var handler = new StubHandler(req => Route(req, _ => StubHandler.Json(DocsJson(("OL1W", "Same Work")))));

        var found = await Catalog(handler).FindAsync([Q.Make("same work", "someone")], default);

        Assert.Equal("OL1W", Assert.Single(found).WorkId);
    }

    [Fact]
    public async Task If_the_free_text_supplement_fails_the_fielded_results_are_kept()
    {
        var handler = new StubHandler(req => req.RequestUri!.AbsolutePath == "/search.json" && IsFreeText(req)
            ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            : Route(req, _ => StubHandler.Json(DocsJson(("OL1W", "Kept")))));

        var found = await Catalog(handler).FindAsync([Q.Make("kept", "someone")], default);

        Assert.Equal("OL1W", Assert.Single(found).WorkId);
    }

    [Fact]
    public async Task Same_work_from_two_hypotheses_is_returned_once()
    {
        var handler = new StubHandler(req => Route(req));

        var found = await Catalog(handler).FindAsync([Q.Make("the hobbit", null), Q.Make(null, "tolkien")], default);

        Assert.Single(found);
    }

    [Theory]
    [InlineData("/works/OL1W?x=1")]
    [InlineData("/works/OL1W#frag")]
    [InlineData("/works/OL1W%2f..%2fadmin")]
    [InlineData("/works/not-an-id")]
    [InlineData("/authors/OL1A")]
    public async Task Docs_whose_key_is_not_a_work_id_are_skipped_and_never_looked_up(string key)
    {
        var handler = new StubHandler(req => Route(req, _ => StubHandler.Json(
            $$"""{"docs":[{"key":"{{key}}","title":"Odd","author_name":["Someone"]}]}""")));

        var found = await Catalog(handler).FindAsync([Q.Make("odd", "someone")], default);

        Assert.Empty(found);
        Assert.DoesNotContain(handler.Requests, r => r.StartsWith("/works/") || r.StartsWith("/authors/"));
    }

    [Theory]
    [InlineData("/authors/OL1A?x=1")]
    [InlineData("/authors/OL1A%2f..%2fadmin")]
    [InlineData("/authors/evil")]
    [InlineData("/works/OL1W")]
    public async Task Author_keys_that_are_not_author_ids_are_ignored_and_never_looked_up(string authorKey)
    {
        var handler = new StubHandler(req => req.RequestUri!.AbsolutePath == "/works/OL1W.json"
            ? StubHandler.Json($$$"""{"authors":[{"author":{"key":"{{{authorKey}}}"}}]}""")
            : Route(req));

        var book = Assert.Single(await Catalog(handler).FindAsync([Q.Make("the hobbit", "tolkien")], default));

        Assert.False(book.Enriched);
        Assert.DoesNotContain(handler.Requests, r => r.StartsWith("/authors/") || r.Contains("admin") || r.Contains("evil"));
    }
}
