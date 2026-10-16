using System.Text.Json;
using FindThatBook.Application.Abstractions;
using FindThatBook.Application.Explanations;
using FindThatBook.Application.Models;
using FindThatBook.Application.Parsing;
using FindThatBook.Application.Search;
using FindThatBook.Tests.Support;

namespace FindThatBook.Tests;

public class McpSearchToolTests
{
    private static readonly CatalogCandidate Hobbit =
        Books.Make("The Hobbit", ["J.R.R. Tolkien"], ["Dixon"], 1937, 120, id: "OL1W") with { CoverId = 99 };

    private static readonly CatalogCandidate Kafka =
        Books.Make("海辺のカフカ", ["村上春樹"], editions: 61, id: "OL2625431W", altTitles: ["Kafka on the Shore"]);

    private sealed class CapturingParser : IQueryParser
    {
        public List<string> Seen { get; } = new();

        public Task<ParseOutcome> ParseAsync(string query, CancellationToken ct)
        {
            lock (Seen) Seen.Add(query);
            return new HeuristicQueryParser().ParseAsync(query, ct);
        }
    }

    private static SearchService Service(IQueryParser? parser = null, params CatalogCandidate[] catalog)
        => new(parser ?? new HeuristicQueryParser(), new FakeCatalog(_ => catalog), new TemplateExplanationWriter());

    private static JsonDocument Parse(ToolReply reply) => JsonDocument.Parse(reply.Text);

    [Fact]
    public async Task Returns_the_search_response_as_json()
    {
        await using var mcp = await McpHarness.StartAsync(Service(null, Hobbit));

        var reply = await mcp.CallAsync("tolkien hobbit 1937");

        Assert.False(reply.IsError);
        using var json = Parse(reply);
        Assert.False(json.RootElement.GetProperty("aiUsed").GetBoolean());
        var result = json.RootElement.GetProperty("results")[0];
        Assert.Equal("The Hobbit", result.GetProperty("title").GetString());
        Assert.Equal("J.R.R. Tolkien", result.GetProperty("author").GetString());
        Assert.Equal("OL1W", result.GetProperty("workId").GetString());
        Assert.Equal("https://openlibrary.org/works/OL1W", result.GetProperty("url").GetString());
        Assert.Equal("https://covers.openlibrary.org/b/id/99-M.jpg", result.GetProperty("coverUrl").GetString());
        Assert.StartsWith("Exact title match", result.GetProperty("explanation").GetString());
    }

    [Fact]
    public async Task Lists_one_read_only_tool_that_requires_a_query()
    {
        await using var mcp = await McpHarness.StartAsync(Service(null, Hobbit));

        var tools = await mcp.ListToolsAsync();

        var tool = Assert.Single(tools);
        Assert.Equal("search_books", tool.Name);
        Assert.True(tool.ProtocolTool.Annotations?.ReadOnlyHint);
        var required = tool.ProtocolTool.InputSchema.GetProperty("required").EnumerateArray().Select(e => e.GetString());
        Assert.Contains("query", required);
    }

    [Fact]
    public async Task Keeps_first_publish_year_and_omits_a_null_message()
    {
        await using var mcp = await McpHarness.StartAsync(Service(null, Hobbit));

        var reply = await mcp.CallAsync("tolkien hobbit 1937");

        using var json = Parse(reply);
        Assert.False(json.RootElement.TryGetProperty("message", out _));
        var result = json.RootElement.GetProperty("results")[0];
        Assert.Equal(1937, result.GetProperty("first_publish_year").GetInt32());
        Assert.False(result.TryGetProperty("firstPublishYear", out _));
    }

    [Fact]
    public async Task Gives_the_service_at_most_200_characters_of_a_very_long_query()
    {
        var parser = new CapturingParser();
        await using var mcp = await McpHarness.StartAsync(Service(parser, Hobbit));

        var reply = await mcp.CallAsync(string.Concat(Enumerable.Repeat("hobbit ", 800)));

        Assert.False(reply.IsError);
        Assert.Equal(200, Assert.Single(parser.Seen).Length);
    }

    [Fact]
    public async Task Returns_a_message_and_no_results_when_nothing_matches_without_an_error()
    {
        await using var mcp = await McpHarness.StartAsync(Service());

        var reply = await mcp.CallAsync("zzzz qqqq");

        Assert.False(reply.IsError);
        using var json = Parse(reply);
        Assert.StartsWith("No matching books found", json.RootElement.GetProperty("message").GetString());
        Assert.Equal(0, json.RootElement.GetProperty("results").GetArrayLength());
    }

    [Fact]
    public async Task Passes_a_non_latin_query_unchanged_and_returns_a_non_latin_title_intact()
    {
        var parser = new CapturingParser();
        await using var mcp = await McpHarness.StartAsync(Service(parser, Kafka));

        await mcp.CallAsync("海辺のカフカ");
        var reply = await mcp.CallAsync("kafka on the shore");

        Assert.Equal("海辺のカフカ", parser.Seen[0]);
        using var json = Parse(reply);
        var result = json.RootElement.GetProperty("results")[0];
        Assert.Equal("海辺のカフカ", result.GetProperty("title").GetString());
        Assert.Equal("村上春樹", result.GetProperty("author").GetString());
    }

    [Fact]
    public async Task Answers_two_searches_in_flight_on_one_connection_with_their_own_results()
    {
        await using var mcp = await McpHarness.StartAsync(Service(null, Hobbit, Kafka));

        var hobbit = mcp.CallAsync("tolkien hobbit 1937");
        var kafka = mcp.CallAsync("kafka on the shore");
        var replies = await Task.WhenAll(hobbit, kafka);

        using var first = Parse(replies[0]);
        using var second = Parse(replies[1]);
        Assert.Equal("The Hobbit", first.RootElement.GetProperty("results")[0].GetProperty("title").GetString());
        Assert.Equal("海辺のカフカ", second.RootElement.GetProperty("results")[0].GetProperty("title").GetString());
    }
}
