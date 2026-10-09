using FindThatBook.Application.Abstractions;
using FindThatBook.Application.Explanations;
using FindThatBook.Application.Models;
using FindThatBook.Application.Parsing;
using FindThatBook.Application.Search;
using FindThatBook.Tests.Support;

namespace FindThatBook.Tests;

public class SearchServiceTests
{
    private sealed class CapturingParser : IQueryParser
    {
        public string? Seen { get; private set; }
        public Task<ParseOutcome> ParseAsync(string query, CancellationToken ct)
        {
            Seen = query;
            return new HeuristicQueryParser().ParseAsync(query, ct);
        }
    }

    private static readonly CatalogCandidate Hobbit =
        Books.Make("The Hobbit", ["J.R.R. Tolkien"], ["Dixon"], 1937, 120, id: "OL1W") with { CoverId = 99 };

    private static SearchService Service(IQueryParser? parser = null, params CatalogCandidate[] catalog)
        => new(parser ?? new HeuristicQueryParser(), new FakeCatalog(_ => catalog), new TemplateExplanationWriter());

    [Fact]
    public async Task Maps_a_match_to_the_response_shape()
    {
        var response = await Service(null, Hobbit).SearchAsync("tolkien hobbit 1937", default);

        var result = Assert.Single(response.Results);
        Assert.Equal("The Hobbit", result.Title);
        Assert.Equal("J.R.R. Tolkien", result.Author);
        Assert.Equal(["J.R.R. Tolkien"], result.Authors);
        Assert.Equal(1937, result.FirstPublishYear);
        Assert.Equal("OL1W", result.WorkId);
        Assert.Equal("https://openlibrary.org/works/OL1W", result.Url);
        Assert.Equal("https://covers.openlibrary.org/b/id/99-M.jpg", result.CoverUrl);
        Assert.StartsWith("Exact title match", result.Explanation);
        Assert.False(response.AiUsed);
        Assert.Null(response.Message);
    }

    [Fact]
    public async Task A_weak_best_match_adds_a_no_confident_match_message_and_keeps_the_results()
    {
        var closeTitle = Books.Make("The Hobbit and Other Stories", ["Someone Else"], id: "OL3W");

        var response = await Service(null, closeTitle).SearchAsync("hobbit stories", default);

        Assert.NotEmpty(response.Results);
        Assert.Contains("closest", response.Message);
    }

    [Fact]
    public async Task A_confident_best_match_has_no_message_even_when_weaker_matches_follow()
    {
        var other = Books.Make("The Hobbit Companion", ["Someone Else"], id: "OL4W");

        var response = await Service(null, Hobbit, other).SearchAsync("tolkien hobbit 1937", default);

        Assert.True(response.Results.Count > 1);
        Assert.Null(response.Message);
    }

    [Fact]
    public async Task No_matches_returns_empty_results_with_a_message()
    {
        var response = await Service(null, Books.Make("Moby Dick", ["Herman Melville"])).SearchAsync("tolkien hobbit", default);

        Assert.Empty(response.Results);
        Assert.False(string.IsNullOrWhiteSpace(response.Message));
    }

    [Fact]
    public async Task Query_without_real_words_returns_empty_results_without_searching()
    {
        var service = new SearchService(
            new HeuristicQueryParser(),
            new FakeCatalog(h => h.Count == 0 ? [] : throw new InvalidOperationException("catalog must not be searched")),
            new TemplateExplanationWriter());

        var response = await service.SearchAsync("1937", default);

        Assert.Empty(response.Results);
        Assert.NotNull(response.Message);
    }

    [Fact]
    public async Task Long_queries_are_trimmed_and_truncated_to_200_characters()
    {
        var parser = new CapturingParser();

        await Service(parser).SearchAsync("   " + new string('a', 500) + "   ", default);

        Assert.Equal(200, parser.Seen!.Length);
    }

    [Fact]
    public async Task Unenriched_candidate_falls_back_to_first_contributor_as_author()
    {
        var unenriched = Books.Make("The Hobbit", [], ["J.R.R. Tolkien"], enriched: false);

        var response = await Service(null, unenriched).SearchAsync("tolkien hobbit", default);

        Assert.Equal("J.R.R. Tolkien", Assert.Single(response.Results).Author);
    }

    [Fact]
    public async Task Catalog_failure_propagates()
    {
        var service = new SearchService(new HeuristicQueryParser(),
            new FakeCatalog(_ => throw new CatalogUnavailableException("down")), new TemplateExplanationWriter());

        await Assert.ThrowsAsync<CatalogUnavailableException>(() => service.SearchAsync("hobbit", default));
    }
}
