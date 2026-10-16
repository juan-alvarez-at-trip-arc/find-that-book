# MCP Server Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Expose the existing search pipeline as one MCP tool, `search_books`, served over stdio by a new `FindThatBook.Mcp` project.

**Architecture:** A console host calls `AddFindThatBook(config)` (Infrastructure) to wire `ISearchService` (Application), then attaches an MCP server with one tool class that forwards to `ISearchService.SearchAsync` and maps two failures to MCP errors. `AddFindThatBookMcp()` registers the tool without a transport, so tests attach an in-memory transport and `Program.cs` attaches stdio.

**Tech Stack:** .NET 10, `ModelContextProtocol` 2.2.0 (C# SDK), `Microsoft.Extensions.Hosting` 10.0.*, xUnit 2.5.3.

**Spec:** `docs/superpowers/specs/2026-10-16-mcp-server-design.md`

## Global Constraints

- Application and Infrastructure do not change.
- Local stdio transport only. One tool, `search_books`. No auth, rate limit, readiness tool, resources, prompts or telemetry.
- Stdout carries JSON-RPC messages only. All logging goes to stderr.
- Settings come from environment variables (`Ai__*`, `OpenLibrary__*`). The host has no settings file.
- Production code under `src/` carries XML doc comments on every class, method, property, enum and interface, and no comments inside method bodies. Tests follow the surrounding style.
- `first_publish_year` stays the only snake_case field (it already is, through `BookResult`).
- No test calls a live API. Write each test so it fails first.
- Commit only when the user asks, only the files named, and no co-author trailer. Each task ends with the commit command to run if and when the user asks.
- Stop any running API or MCP process before `dotnet test`, because they lock the DLLs.
- Tool names and messages (copy exactly):
  - tool name `search_books`
  - empty query: `The query is empty. Describe the book with a title, an author or both.`
  - catalog down: `Open Library is unavailable or cooling down. Try again in a minute.`
  - generic SDK error text: `An error occurred invoking 'search_books'.`

## Review Focus

Failure modes the spec implies that no task would otherwise test, most likely first. Each has a test in the task named.

1. A whitespace-only query (`"   "`, a tab and newline) reaches the tool: it must return the empty-query error and never call the search service (Task 2).
2. A 5,000-character query: it must succeed and the service must see 200 characters (Task 1).
3. A search with no match: it must be a normal result with the "No matching books found" message and an empty list, not an error (Task 1).
4. A non-Latin query and a non-Latin title (`海辺のカフカ`): the query reaches the parser unchanged and the title survives the JSON round trip (Task 1).
5. Two searches in flight on one connection: each gets its own results (Task 1).

## File Structure

| File | Responsibility |
|---|---|
| `src/FindThatBook.Mcp/FindThatBook.Mcp.csproj` | Console project and package references |
| `src/FindThatBook.Mcp/BookSearchTools.cs` | The `search_books` tool and its two error messages |
| `src/FindThatBook.Mcp/McpServiceCollectionExtensions.cs` | `AddFindThatBookMcp()` |
| `src/FindThatBook.Mcp/Program.cs` | Host, stderr logging, stdio transport |
| `tests/FindThatBook.Tests/Support/McpHarness.cs` | In-memory MCP server and client for tests |
| `tests/FindThatBook.Tests/McpSearchToolTests.cs` | Task 1 tests |
| `tests/FindThatBook.Tests/McpSearchToolErrorTests.cs` | Task 2 tests |
| `tests/FindThatBook.Tests/McpProcessTests.cs` | Task 3 test |
| `tests/FindThatBook.Tests/FindThatBook.Tests.csproj` | Modified: project reference to Mcp |
| `FindThatBook.sln` | Modified: new project |
| `README.md`, `CLAUDE.md` | Modified in Task 4 |

---

### Task 1: Project, harness and the happy path

**Files:**
- Create: `src/FindThatBook.Mcp/FindThatBook.Mcp.csproj`, `src/FindThatBook.Mcp/BookSearchTools.cs`, `src/FindThatBook.Mcp/McpServiceCollectionExtensions.cs`, `src/FindThatBook.Mcp/Program.cs` (a stub that Task 3 completes)
- Create: `tests/FindThatBook.Tests/Support/McpHarness.cs`, `tests/FindThatBook.Tests/McpSearchToolTests.cs`
- Modify: `tests/FindThatBook.Tests/FindThatBook.Tests.csproj`, `FindThatBook.sln`

**Interfaces:**
- Consumes: `ISearchService.SearchAsync(string, CancellationToken)` returning `SearchResponse` (Application).
- Produces:
  - `FindThatBook.Mcp.BookSearchTools(ISearchService search)` with `Task<SearchResponse> SearchBooksAsync(string query, CancellationToken ct)`, public constants `EmptyQueryMessage` and `CatalogUnavailableMessage`.
  - `FindThatBook.Mcp.McpServiceCollectionExtensions.AddFindThatBookMcp(this IServiceCollection services)` returning `IMcpServerBuilder`.
  - `FindThatBook.Tests.Support.McpHarness.StartAsync(ISearchService search)`, `CallAsync(string query)` returning `ToolReply(bool IsError, string Text)`, `ListToolsAsync()`, `DisposeAsync()`.

- [ ] **Step 1: Create the project and register it**

Create `src/FindThatBook.Mcp/FindThatBook.Mcp.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\FindThatBook.Application\FindThatBook.Application.csproj" />
    <ProjectReference Include="..\FindThatBook.Infrastructure\FindThatBook.Infrastructure.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.Hosting" Version="10.0.*" />
    <PackageReference Include="ModelContextProtocol" Version="2.2.0" />
  </ItemGroup>

</Project>
```

Create the stub `src/FindThatBook.Mcp/Program.cs` so the project builds:

```csharp
Console.Error.WriteLine("FindThatBook.Mcp is not wired yet.");
```

Add it to the solution and the test project:

```bash
dotnet sln FindThatBook.sln add src/FindThatBook.Mcp/FindThatBook.Mcp.csproj --solution-folder src
```

In `tests/FindThatBook.Tests/FindThatBook.Tests.csproj`, add inside the existing `ProjectReference` item group:

```xml
    <ProjectReference Include="..\..\src\FindThatBook.Mcp\FindThatBook.Mcp.csproj" />
```

Run: `dotnet build`
Expected: build succeeds with 0 errors.

- [ ] **Step 2: Write the harness**

Create `tests/FindThatBook.Tests/Support/McpHarness.cs`:

```csharp
using System.IO.Pipelines;
using FindThatBook.Application.Abstractions;
using FindThatBook.Mcp;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace FindThatBook.Tests.Support;

/// <summary>The outcome of one tool call: whether the server flagged an error and the text of the first content block.</summary>
public sealed record ToolReply(bool IsError, string Text);

/// <summary>An MCP server built from <c>AddFindThatBookMcp</c> and an MCP client joined by in-memory pipes.</summary>
public sealed class McpHarness : IAsyncDisposable
{
    private readonly ServiceProvider _provider;
    private readonly McpClient _client;

    private McpHarness(ServiceProvider provider, McpClient client)
    {
        _provider = provider;
        _client = client;
    }

    public static async Task<McpHarness> StartAsync(ISearchService search)
    {
        var clientToServer = new Pipe();
        var serverToClient = new Pipe();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(search);
        services.AddFindThatBookMcp()
            .WithStreamServerTransport(clientToServer.Reader.AsStream(), serverToClient.Writer.AsStream());
        var provider = services.BuildServiceProvider();
        _ = provider.GetRequiredService<McpServer>().RunAsync();
        var client = await McpClient.CreateAsync(
            new StreamClientTransport(clientToServer.Writer.AsStream(), serverToClient.Reader.AsStream()));
        return new McpHarness(provider, client);
    }

    public async Task<ToolReply> CallAsync(string query)
    {
        var result = await _client.CallToolAsync("search_books", new Dictionary<string, object?> { ["query"] = query });
        var text = result.Content.OfType<TextContentBlock>().Single().Text;
        return new ToolReply(result.IsError == true, text);
    }

    public async Task<IList<McpClientTool>> ListToolsAsync() => await _client.ListToolsAsync();

    public async ValueTask DisposeAsync()
    {
        await _client.DisposeAsync();
        await _provider.DisposeAsync();
    }
}
```

- [ ] **Step 3: Write the failing tests**

Create `tests/FindThatBook.Tests/McpSearchToolTests.cs`:

```csharp
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
```

- [ ] **Step 4: Run the tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~McpSearchToolTests"`
Expected: build FAILS with `CS1061` or `CS0103` for `AddFindThatBookMcp` (it does not exist yet).

- [ ] **Step 5: Write the minimal implementation**

Create `src/FindThatBook.Mcp/BookSearchTools.cs`:

```csharp
using System.ComponentModel;
using FindThatBook.Application.Abstractions;
using FindThatBook.Application.Models;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace FindThatBook.Mcp;

/// <summary>The MCP tools of the server. The only tool searches Open Library for the book a reader describes.</summary>
/// <param name="search">The search pipeline shared with the API.</param>
[McpServerToolType]
public sealed class BookSearchTools(ISearchService search)
{
    /// <summary>The error shown when the query is blank.</summary>
    public const string EmptyQueryMessage = "The query is empty. Describe the book with a title, an author or both.";
    /// <summary>The error shown when Open Library cannot be searched.</summary>
    public const string CatalogUnavailableMessage = "Open Library is unavailable or cooling down. Try again in a minute.";

    /// <summary>Finds up to 5 ranked books for a messy description and explains each match in one sentence.</summary>
    /// <param name="query">What the reader remembers: a title, an author, both with extra words, a misspelling or a year.</param>
    /// <param name="ct">Token that cancels the search.</param>
    /// <returns>The ranked results, whether the AI was used end to end, and a notice when the best match is weak or nothing matched.</returns>
    [McpServerTool(Name = "search_books", ReadOnly = true)]
    [Description("Finds books from a messy or partial description: a title, an author, both with extra words, misspellings or a year (for example 'tolkien hobbit illustrated 1937'). Returns up to 5 ranked Open Library matches, each with a one-sentence explanation. Not for ISBNs or long text. Only the first 200 characters are used.")]
    public async Task<SearchResponse> SearchBooksAsync(
        [Description("What the reader remembers about the book.")] string query,
        CancellationToken ct)
        => await search.SearchAsync(query, ct);
}
```

Create `src/FindThatBook.Mcp/McpServiceCollectionExtensions.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;

namespace FindThatBook.Mcp;

/// <summary>Dependency injection registration for the MCP server.</summary>
public static class McpServiceCollectionExtensions
{
    /// <summary>Registers the MCP server and the book search tools. It adds no transport, so the caller chooses one. <c>ISearchService</c> must be registered separately.</summary>
    /// <param name="services">The service collection to add registrations to.</param>
    /// <returns>The MCP server builder, for attaching a transport.</returns>
    public static IMcpServerBuilder AddFindThatBookMcp(this IServiceCollection services)
        => services.AddMcpServer().WithTools<BookSearchTools>();
}
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~McpSearchToolTests"`
Expected: 7 passed. If `tool.ProtocolTool.InputSchema.GetProperty` does not compile, `InputSchema` is not a `JsonElement` in this SDK version: read its type with F12 or `dotnet build` output and adjust only that line of the test.

- [ ] **Step 7: Checkpoint**

Run: `dotnet test`
Expected: all tests pass (existing 590 plus 7).

If the user has asked for commits:

```bash
git add FindThatBook.sln src/FindThatBook.Mcp tests/FindThatBook.Tests/FindThatBook.Tests.csproj tests/FindThatBook.Tests/Support/McpHarness.cs tests/FindThatBook.Tests/McpSearchToolTests.cs
git commit -m "feat: add the MCP project with the search_books tool"
```

---

### Task 2: Error mapping and the AI fallback

**Files:**
- Create: `tests/FindThatBook.Tests/McpSearchToolErrorTests.cs`
- Modify: `src/FindThatBook.Mcp/BookSearchTools.cs`

**Interfaces:**
- Consumes: `McpHarness`, `ToolReply` (Task 1), `BookSearchTools.EmptyQueryMessage`, `BookSearchTools.CatalogUnavailableMessage`, `CatalogUnavailableException` and `AiUnavailableException` (Application.Abstractions).
- Produces: the final behavior of `BookSearchTools.SearchBooksAsync`.

- [ ] **Step 1: Write the failing tests**

Create `tests/FindThatBook.Tests/McpSearchToolErrorTests.cs`:

```csharp
using System.Text.Json;
using FindThatBook.Application.Abstractions;
using FindThatBook.Application.Explanations;
using FindThatBook.Application.Models;
using FindThatBook.Application.Parsing;
using FindThatBook.Application.Search;
using FindThatBook.Infrastructure.Ai;
using FindThatBook.Mcp;
using FindThatBook.Tests.Support;
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
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~McpSearchToolErrorTests"`
Expected: the blank-query tests FAIL (`Assert.True() Failure`, `reply.IsError` false or `Calls` is 1), and the unavailable-catalog test FAILS (the reply is the generic error, which does not contain the retry message). The AI-fallback and unexpected-failure tests already pass, because they pin behavior the SDK and the decorators already give. Keep them as regression tests.

- [ ] **Step 3: Write the implementation**

Replace the body of `SearchBooksAsync` in `src/FindThatBook.Mcp/BookSearchTools.cs` and add the `<exception>` docs:

```csharp
    /// <exception cref="McpException">The query is blank, or Open Library cannot be searched. The message is shown to the client.</exception>
    [McpServerTool(Name = "search_books", ReadOnly = true)]
    [Description("Finds books from a messy or partial description: a title, an author, both with extra words, misspellings or a year (for example 'tolkien hobbit illustrated 1937'). Returns up to 5 ranked Open Library matches, each with a one-sentence explanation. Not for ISBNs or long text. Only the first 200 characters are used.")]
    public async Task<SearchResponse> SearchBooksAsync(
        [Description("What the reader remembers about the book.")] string query,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query)) throw new McpException(EmptyQueryMessage);
        try
        {
            return await search.SearchAsync(query, ct);
        }
        catch (CatalogUnavailableException)
        {
            throw new McpException(CatalogUnavailableMessage);
        }
    }
```

Keep the existing `<summary>`, `<param>` and `<returns>` doc lines above the attributes, and place the new `<exception>` line with them.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~Mcp"`
Expected: 13 passed (7 from Task 1, 6 here: 3 blank-query cases plus 3 others).

- [ ] **Step 5: Checkpoint**

Run: `dotnet test`
Expected: all pass.

If the user has asked for commits:

```bash
git add src/FindThatBook.Mcp/BookSearchTools.cs tests/FindThatBook.Tests/McpSearchToolErrorTests.cs
git commit -m "feat: map blank queries and an unavailable catalog to MCP errors"
```

---

### Task 3: The stdio host and the stdout check

**Files:**
- Modify: `src/FindThatBook.Mcp/Program.cs`
- Create: `tests/FindThatBook.Tests/McpProcessTests.cs`

**Interfaces:**
- Consumes: `AddFindThatBook(IConfiguration)` (Infrastructure), `AddFindThatBookMcp()` (Task 1).
- Produces: the runnable server `FindThatBook.Mcp.dll`, which speaks MCP over stdio.

- [ ] **Step 1: Write the failing test**

Create `tests/FindThatBook.Tests/McpProcessTests.cs`:

```csharp
using System.Diagnostics;
using System.Text.Json;

namespace FindThatBook.Tests;

public class McpProcessTests
{
    private const string Initialize = """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"test","version":"1"}}}""";
    private const string Initialized = """{"jsonrpc":"2.0","method":"notifications/initialized"}""";
    private const string ListTools = """{"jsonrpc":"2.0","id":2,"method":"tools/list"}""";

    [Fact]
    public async Task Built_server_writes_only_protocol_messages_to_stdout()
    {
        var dll = Path.Combine(AppContext.BaseDirectory, "FindThatBook.Mcp.dll");
        Assert.True(File.Exists(dll), $"{dll} was not copied next to the test binaries.");
        var start = new ProcessStartInfo("dotnet", $"\"{dll}\"")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        using var process = Process.Start(start)!;
        process.ErrorDataReceived += (_, _) => { };
        process.BeginErrorReadLine();

        await process.StandardInput.WriteLineAsync(Initialize);
        await process.StandardInput.WriteLineAsync(Initialized);
        await process.StandardInput.WriteLineAsync(ListTools);
        await process.StandardInput.FlushAsync();

        var lines = new List<string>();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            while (lines.Count < 2)
            {
                var line = await process.StandardOutput.ReadLineAsync(timeout.Token);
                if (line is null) break;
                lines.Add(line);
            }
        }
        finally
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
        }

        Assert.Equal(2, lines.Count);
        foreach (var line in lines)
        {
            using var message = JsonDocument.Parse(line);
            Assert.Equal("2.0", message.RootElement.GetProperty("jsonrpc").GetString());
        }
        Assert.Contains("search_books", lines[1]);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~McpProcessTests"`
Expected: FAIL. The stub `Program.cs` writes to stderr and exits, so stdout has no lines and `Assert.Equal(2, lines.Count)` fails (`Expected: 2, Actual: 0`).

If it fails earlier with "was not copied next to the test binaries", the project reference did not copy the dll. Use `Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "FindThatBook.Mcp", "bin", AppContext.BaseDirectory.Contains("Release") ? "Release" : "Debug", "net10.0", "FindThatBook.Mcp.dll")` instead, and build the Mcp project first.

- [ ] **Step 3: Write the implementation**

Replace `src/FindThatBook.Mcp/Program.cs`:

```csharp
using FindThatBook.Infrastructure;
using FindThatBook.Mcp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
builder.Services.AddFindThatBook(builder.Configuration);
builder.Services.AddFindThatBookMcp().WithStdioServerTransport();

await builder.Build().RunAsync();
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~McpProcessTests"`
Expected: 1 passed. The test sends no search, so it makes no network call.

- [ ] **Step 5: Optional live check by hand (calls Open Library once, and the AI if a key is set)**

Ask the user before running it.

```bash
dotnet build src/FindThatBook.Mcp -c Release
```

Then register the server in Claude Code and ask it to find a book:

```bash
claude mcp add find-that-book --env OpenLibrary__Contact=<email> -- dotnet src/FindThatBook.Mcp/bin/Release/net10.0/FindThatBook.Mcp.dll
```

Expected: `search_books` appears in `/mcp`, and "find the book 'tolkien hobbit 1937'" returns The Hobbit first. With no AI key the answer carries `aiUsed: false`. Remove it afterwards with `claude mcp remove find-that-book`.

- [ ] **Step 6: Checkpoint**

Run: `dotnet test`
Expected: all pass.

If the user has asked for commits:

```bash
git add src/FindThatBook.Mcp/Program.cs tests/FindThatBook.Tests/McpProcessTests.cs
git commit -m "feat: serve the MCP tool over stdio"
```

---

### Task 4: Documentation

**Files:**
- Modify: `README.md`, `CLAUDE.md`

**Interfaces:**
- Consumes: the finished server from Task 3.
- Produces: nothing for later tasks.

- [ ] **Step 1: Update the README project layout**

In `README.md`, in the `## Project layout` code block, add this entry after the `FindThatBook.Infrastructure/` entry (before `tests/`):

```
  FindThatBook.Mcp/             stdio MCP server with one tool, search_books, on the same search pipeline
```

- [ ] **Step 2: Add the README section**

In `README.md`, insert this section directly before `## Project layout`:

```markdown
## MCP server

`src/FindThatBook.Mcp` serves the search as an MCP tool over stdio, so an MCP client such as Claude Code or Claude Desktop can find a book from a messy description. It runs the same pipeline as the API (parse, Open Library search, deterministic ranking, explanations, AI fallback). It has no auth, no rate limit and no HTTP transport, so run it only as a local process of your own client.

Tool: `search_books(query)`. It returns the same shape as `POST /api/search` (`aiUsed`, `message` and up to 5 `results`, with `first_publish_year` in snake_case). Only the first 200 characters of the query are used. A blank query and an unavailable Open Library come back as tool errors, and the Open Library limits, cooldown and circuit breaker apply as in the API.

Settings are environment variables in the client's MCP configuration (`Ai__Provider`, `Ai__Gemini__ApiKey`, `Ai__Model`, `OpenLibrary__Contact` and the rest, see [AI providers](#ai-providers)). `dotnet user-secrets` and `.env` are not read. Without a key it runs in fallback mode.

    dotnet build src/FindThatBook.Mcp -c Release
    claude mcp add find-that-book --env Ai__Gemini__ApiKey=<your key> --env OpenLibrary__Contact=<your email> -- dotnet src/FindThatBook.Mcp/bin/Release/net10.0/FindThatBook.Mcp.dll

Run the built dll, not `dotnet run`: a build step before the server starts is slow and was not checked for stray stdout output. Logs go to stderr, because stdout carries only protocol messages.
```

- [ ] **Step 3: Update CLAUDE.md**

In `CLAUDE.md`, under `## Layout`, add this bullet after the `src/FindThatBook.Api` bullet:

```markdown
- `src/FindThatBook.Mcp`: stdio MCP server with one tool, `search_books`, on `ISearchService`. It calls `AddFindThatBook` for composition and has no settings file (defaults live in the options classes).
```

Under `## Rules for changes`, add this rule after the rate-limiting rule:

```markdown
- The MCP server (`FindThatBook.Mcp`) writes only JSON-RPC to stdout: logging goes to stderr, and nothing may call `Console.Write` or `Console.WriteLine`. It has one tool, `search_books`. `McpProcessTests` checks stdout, and a tool change needs a test through `McpHarness` first.
```

- [ ] **Step 4: Verify the docs against the code**

Run: `dotnet test`
Expected: all pass (`RepoHardeningTests` and the other file-reading tests still pass).

Grep check: `grep -n "FindThatBook.Mcp" README.md CLAUDE.md` shows the layout entry, the section and the rule.

- [ ] **Step 5: Checkpoint**

If the user has asked for commits:

```bash
git add README.md CLAUDE.md docs/superpowers/specs/2026-10-16-mcp-server-design.md docs/superpowers/plans/2026-10-16-mcp-server.md
git commit -m "docs: document the MCP server"
```

---

## Self-review

- **Spec coverage:** D1 to D4 and every component are covered. T1, T6 and T7 are Task 1 (`Returns_the_search_response_as_json`, `Lists_one_read_only_tool_that_requires_a_query`, `Keeps_first_publish_year_and_omits_a_null_message`). T2 to T5 are Task 2. T8 is Task 3. T9 and the settings file were dropped from the spec: a second file would collide with the API's `appsettings.json` in the test output and override environment variables, and the options classes already hold the defaults. The spec says so. The README and `CLAUDE.md` changes are Task 4.
- **Deviation from the spec text:** `AddFindThatBookMcp()` returns the builder and adds no transport (spec edited to match), so tests can use an in-memory transport.
- **Placeholders:** none. The one conditional is the T8 dll path fallback, which has its replacement code.
- **Type consistency:** `BookSearchTools`, `SearchBooksAsync`, `EmptyQueryMessage`, `CatalogUnavailableMessage`, `AddFindThatBookMcp`, `McpHarness.StartAsync/CallAsync/ListToolsAsync` and `ToolReply` are named identically in every task.
- **Unverified until run:** `tool.ProtocolTool.InputSchema` being a `JsonElement` (Task 1, Step 6 says what to do), and the test project copying `FindThatBook.Mcp.dll` (Task 3, Step 2 has the fallback).
