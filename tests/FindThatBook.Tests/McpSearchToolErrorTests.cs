using System.Text.Json;
using FindThatBook.Application.Abstractions;
using FindThatBook.Application.Explanations;
using FindThatBook.Application.Models;
using FindThatBook.Application.Parsing;
using FindThatBook.Application.Search;
using FindThatBook.Infrastructure.Ai;
using FindThatBook.Mcp;
using FindThatBook.Tests.Support;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace FindThatBook.Tests;

public class McpSearchToolErrorTests
{
    private const string GenericError = "An error occurred invoking 'search_books'.";

    private static readonly CatalogCandidate Hobbit =
        Books.Make("The Hobbit", ["J.R.R. Tolkien"], ["Dixon"], 1937, 120, id: "OL1W");

    private sealed class CountingSearch : ISearchService
    {
        public int Calls { get; private set; }

        public Task<SearchResponse> SearchAsync(string query, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(new SearchResponse(false, null, []));
        }
    }

    private sealed class ThrowingCatalog(Exception error) : IBookCatalog
    {
        public Task<IReadOnlyList<CatalogCandidate>> FindAsync(IReadOnlyList<ParsedQuery> hypotheses, CancellationToken ct)
            => throw error;
    }

    private static SearchService ServiceWith(IBookCatalog catalog)
        => new(new HeuristicQueryParser(), catalog, new TemplateExplanationWriter());

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n ")]
    public async Task Rejects_a_blank_query_without_calling_the_search(string query)
    {
        var search = new CountingSearch();
        await using var mcp = await McpHarness.StartAsync(search);

        var reply = await mcp.CallAsync(query);

        Assert.True(reply.IsError);
        Assert.Contains(BookSearchTools.EmptyQueryMessage, reply.Text);
        Assert.Equal(0, search.Calls);
    }

    [Fact]
    public async Task Reports_an_unavailable_catalog_with_a_retry_hint_and_without_its_detail()
    {
        var catalog = new ThrowingCatalog(new CatalogUnavailableException("HTTP 429 from https://openlibrary.org/search.json"));
        await using var mcp = await McpHarness.StartAsync(ServiceWith(catalog));

        var reply = await mcp.CallAsync("tolkien hobbit");

        Assert.True(reply.IsError);
        Assert.Contains(BookSearchTools.CatalogUnavailableMessage, reply.Text);
        Assert.DoesNotContain("429", reply.Text);
    }

    [Fact]
    public async Task Returns_results_with_ai_used_false_when_the_ai_is_unavailable()
    {
        var ai = new FakeAiClient(_ => throw new AiUnavailableException("provider down"));
        var service = new SearchService(
            new FallbackQueryParser(new AiQueryParser(ai), new HeuristicQueryParser(), NullLogger<FallbackQueryParser>.Instance),
            new FakeCatalog(_ => [Hobbit]),
            new FallbackExplanationWriter(new AiExplanationWriter(ai), new TemplateExplanationWriter(), NullLogger<FallbackExplanationWriter>.Instance));
        await using var mcp = await McpHarness.StartAsync(service);

        var reply = await mcp.CallAsync("tolkien hobbit 1937");

        Assert.False(reply.IsError);
        using var json = JsonDocument.Parse(reply.Text);
        Assert.False(json.RootElement.GetProperty("aiUsed").GetBoolean());
        Assert.Equal("The Hobbit", json.RootElement.GetProperty("results")[0].GetProperty("title").GetString());
    }

    private sealed class CapturingLogs : ILoggerProvider
    {
        public List<Exception> Errors { get; } = new();

        public ILogger CreateLogger(string categoryName) => new Capture(Errors);

        public void Dispose() { }

        private sealed class Capture(List<Exception> errors) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (exception is not null) lock (errors) errors.Add(exception);
            }
        }
    }

    [Fact]
    public async Task Logs_the_detail_of_an_unexpected_failure_for_the_operator()
    {
        var logs = new CapturingLogs();
        var catalog = new ThrowingCatalog(new InvalidOperationException("secret detail"));
        await using var mcp = await McpHarness.StartAsync(ServiceWith(catalog), logs);

        await mcp.CallAsync("tolkien hobbit");

        Assert.Contains(logs.Errors, e => e.Message == "secret detail");
    }

    [Fact]
    public async Task Hides_the_detail_of_an_unexpected_failure()
    {
        var catalog = new ThrowingCatalog(new InvalidOperationException("secret detail"));
        await using var mcp = await McpHarness.StartAsync(ServiceWith(catalog));

        var reply = await mcp.CallAsync("tolkien hobbit");

        Assert.True(reply.IsError);
        Assert.Equal(GenericError, reply.Text);
        Assert.DoesNotContain("secret detail", reply.Text);
    }
}
