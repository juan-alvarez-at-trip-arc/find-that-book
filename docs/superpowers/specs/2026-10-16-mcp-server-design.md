# MCP server design

Date: 2026-10-16. Status: implemented. See "As built" at the end for what changed during implementation.

## Goal

Expose "find that book" as an MCP tool, so an MCP client such as Claude Code or Claude Desktop can resolve a messy, half-remembered book description into up to 5 ranked Open Library matches with explanations.

## Decisions

- D1: Local stdio transport only. No auth, rate limit, security headers or Content-Security-Policy work. A remote HTTP transport is out of scope.
- D2: One tool, `search_books(query)`. No readiness tool, resources or prompts.
- D3: A new project, `src/FindThatBook.Mcp`. It consumes `ISearchService` from `FindThatBook.Application`. Application and Infrastructure do not change.
- D4: The existing `FindThatBook.Api` does not gain a stdio mode, and the MCP server does not call the HTTP API.

## Assumption corrected during design

Application has no I/O. `IBookCatalog` and the AI clients live in `FindThatBook.Infrastructure`, wired by `AddFindThatBook(config)`. The MCP project references Application for the types and `ISearchService`, and Infrastructure only for that composition call. It uses no Infrastructure type directly.

## Verified before writing this spec

- R1 (SDK): `ModelContextProtocol` 2.2.0 compiles and runs on net10.0 with `AddMcpServer().WithStdioServerTransport().WithTools<T>()`, `[McpServerToolType]`, `[McpServerTool(Name, ReadOnly)]` and `[Description]`. A probe over stdio showed:
  - `tools/list` returns the tool with an input schema (`query` required) and `readOnlyHint: true`.
  - An `McpException` message reaches the client as an `isError` result: `An error occurred invoking 'search_books': <message>`.
  - Any other exception reaches the client as the generic `An error occurred invoking 'search_books'.` with no detail.
  - A record return value is serialized as JSON text in one content block, with camelCase names, `[JsonPropertyName("first_publish_year")]` kept and null properties omitted (so `message` is absent when null).
  - With `ClearProviders()` and a console logger sending every level to stderr, stdout carries only JSON-RPC lines.
- R2 (composition): `AddFindThatBook` and the Infrastructure project use no ASP.NET type, so a generic host can call it. HTTP body logging and telemetry are registered by the API (`TelemetryExtensions`), not by `AddFindThatBook`, so the MCP host logs no HTTP traffic unless telemetry is added later.

## Components

| Piece | Purpose |
|---|---|
| `src/FindThatBook.Mcp/FindThatBook.Mcp.csproj` | Console project, `net10.0`, references Application, Infrastructure, `ModelContextProtocol` 2.2.0 and `Microsoft.Extensions.Hosting` 10.0.* |
| `Program.cs` | Builds the generic host, clears logging providers, sends all logs to stderr, calls `AddFindThatBook(configuration)` and `AddFindThatBookMcp().WithStdioServerTransport()`, runs the host |
| `McpServiceCollectionExtensions.AddFindThatBookMcp` | Registers `AddMcpServer()` and `WithTools<BookSearchTools>()` and returns the `IMcpServerBuilder`. It adds no transport, so tests attach an in-memory transport and `Program.cs` attaches stdio |
| `BookSearchTools.SearchBooksAsync` | The `search_books` tool. Takes `ISearchService` from DI |

## The tool

- Name `search_books`, read-only. Argument `query` (string, required).
- Description for the client: finds books from a messy or partial description (a title, an author, both with extra words, misspellings, a year). Not for ISBNs or long text. Only the first 200 characters are used.
- A blank or whitespace query throws `McpException("The query is empty. Describe the book with a title, an author or both.")`.
- Otherwise it returns the `SearchResponse` from `ISearchService.SearchAsync(query, ct)` unchanged: `aiUsed`, `message` (absent when null) and `results`, each with `title`, `author`, `authors`, `first_publish_year`, `workId`, `url`, `coverUrl`, `explanation`, `matchTier`, `exactTitle` and `matchedTitle`. `first_publish_year` stays the only snake_case field.
- `CatalogUnavailableException` becomes `McpException("Open Library is unavailable or cooling down. Try again in a minute.")`, matching the API's 502.
- Every other exception is left to the SDK, which returns the generic tool error. The exception is logged to stderr.
- `AiUnavailableException` never reaches the tool, because the fallback decorators handle it. The response then carries `aiUsed: false`.
- No rate limit is added. The Open Library gate, 429 cooldown and circuit breaker in Infrastructure still apply, and stdio serves one local client.

## Configuration and use

- Settings come from environment variables set in the client's MCP configuration (`Ai__Provider`, `Ai__Gemini__ApiKey`, `Ai__Model`, `OpenLibrary__Contact` and the rest). `dotnet user-secrets` is not available for this host. The host has no settings file: every default lives in the options classes, which `AppSettingsTests` already checks against the API's `appsettings.json`. A second settings file would collide with the API's in the test output and, added after the environment variables, would override them.
- Example: `claude mcp add find-that-book --env Ai__Gemini__ApiKey=<key> --env OpenLibrary__Contact=<email> -- dotnet run --project src/FindThatBook.Mcp`.
- Without a key the server still works in fallback mode.
- `dotnet run` builds first, which slows startup and may write build output that a client could misread. Not verified. The README recommends `dotnet build -c Release` once and `dotnet src/FindThatBook.Mcp/bin/Release/net10.0/FindThatBook.Mcp.dll` for clients.
- `README.md` gets a short "MCP server" section and the project layout list gets the new project. `CLAUDE.md` gets one line under Layout and one rule: stdout carries protocol messages only.

## Tests

Added to `tests/FindThatBook.Tests`, which gains a project reference to `FindThatBook.Mcp`. Each test is written to fail first. None calls a live API. The in-memory tests (T1 to T7) attach an SDK stream transport to the server from `AddFindThatBookMcp()`.

1. T1: a tool call with a `FakeCatalog` and `FakeAiClient` returns the same results as `SearchService`, through an in-memory client and server pair (SDK stream transport).
2. T2: a blank query returns an error result with the empty-query message.
3. T3: a catalog that throws `CatalogUnavailableException` returns an error result with the unavailable message.
4. T4: an AI client that throws `AiUnavailableException` still returns results with `aiUsed: false`.
5. T5: a catalog that throws an unexpected exception returns the generic error and the message text is not in the result.
6. T6: the tool list holds exactly one tool, `search_books`, read-only, with `query` required.
7. T7: the serialized result contains `first_publish_year` and no `message` property when the message is null.
8. T8: the built server started as a process answers `initialize` and `tools/list`, and every stdout line is a JSON-RPC message. It sends no search, so it makes no network call.

## Risks

- R3: the SDK is at 2.x and changes quickly. The package version is pinned, and T6 to T8 fail on a breaking change.
- R4: T8 needs the built Mcp dll next to the test binaries. A project reference from the test project copies it. If it does not copy the runtime config, the plan switches T8 to the `dotnet <dll>` path under `src/FindThatBook.Mcp/bin`.
- R5: a client that parses the JSON text in the content block depends on the field names above. They match the API, so a rename in `BookResult` breaks both, and T1 and T7 catch it.

## As built

- The host builds with `ContentRootPath = AppContext.BaseDirectory`. `Host.CreateApplicationBuilder(args)` otherwise reads `appsettings.json` from the working directory, which contradicts "no settings file". T9 below covers it.
- R4 happened: the dll next to the test binaries crashes (it lacks `Microsoft.Extensions.Hosting`), so T8 runs the dll from `src/FindThatBook.Mcp/bin/<configuration>/net10.0`. The configuration is the folder name of the test output, and `dotnet test` builds the Mcp project through the project reference.
- T8 sends `initialize`, waits for its reply, then sends `notifications/initialized` and `tools/list`. Sending all three at once made the reply order depend on timing.
- T9: a server started in a folder with an `appsettings.json` that names an invalid AI provider still answers `initialize` and `tools/list`.
- T10: an unexpected exception is logged (the SDK does it) while the client gets only the generic error. `McpHarness.StartAsync` takes an optional `ILoggerProvider` for this.
- Not run: a real MCP client, a live Open Library search and a live AI provider.

## Out of scope

Remote HTTP transport and auth, a readiness tool, resources and prompts, telemetry in the MCP host, a Docker image for it, structured output schemas and any change to ranking or the AI steps.
