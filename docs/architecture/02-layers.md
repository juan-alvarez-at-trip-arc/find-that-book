# Layers and dependencies

Arrows point from the dependent project to the project it uses.

```mermaid
flowchart TB
    subgraph web["web/ (React, Vite, TypeScript)"]
        App --> lib_api["lib/api.ts"]
        App --> FeaturedResults
        App --> EmptyShelf
        App --> SearchProgress
        App --> ThemeToggle
        App --> Toast
    end

    subgraph Api["FindThatBook.Api"]
        SearchController
        HealthController
        ApiExceptionHandler
        Program["Program.cs (static files, DI)"]
        TelemetryExt["TelemetryExtensions (OpenTelemetry)"]
    end

    subgraph Mcp["FindThatBook.Mcp (stdio, one tool)"]
        BookSearchTools["BookSearchTools<br/>search_books"]
        McpExt["McpServiceCollectionExtensions<br/>AddFindThatBookMcp"]
        McpProgram["Program.cs (stderr logging, stdio transport)"]
    end

    subgraph Infra["FindThatBook.Infrastructure"]
        OpenLibraryCatalog
        OpenLibraryGate
        OLPipe["OpenLibraryResiliencePipeline<br/>(Polly: retry, circuit breaker, timeout)"]
        AiPipe["AiResiliencePipeline<br/>(Polly: total timeout, retry,<br/>circuit breaker, attempt timeout)"]
        ResOptions["AiResilienceOptions<br/>(Ai:Resilience section)"]
        AiQueryParser
        AiExplanationWriter
        IAiClient{{IAiClient}}
        GeminiClient
        OpenAiCompatibleClient
        GenOptions["AiGenerationOptions<br/>(Ai section: temperature, top-k,<br/>top-p, seed, reasoning effort)"]
        Health["Health probes and report"]
        HttpLog["HttpLoggingHandler, SensitiveDataMasker"]
        DI["ServiceCollectionExtensions"]
    end

    subgraph App_["FindThatBook.Application (no I/O)"]
        SearchService
        Matcher
        Normalizer
        HeuristicQueryParser
        TemplateExplanationWriter
        FallbackQueryParser
        FallbackExplanationWriter
        Abstractions{{IQueryParser, IBookCatalog, IExplanationWriter, ISearchService}}
        FindThatBookTelemetry["FindThatBookTelemetry (ActivitySource, Meter)"]
    end

    lib_api -- "HTTP" --> SearchController
    SearchController --> SearchService
    HealthController --> Health
    BookSearchTools --> SearchService
    McpProgram --> McpExt
    McpProgram --> DI
    McpExt --> BookSearchTools
    Mcp --> Infra
    Mcp --> App_
    Program --> DI
    Program --> TelemetryExt
    TelemetryExt --> HttpLog
    SearchService --> FindThatBookTelemetry
    FallbackQueryParser --> FindThatBookTelemetry
    FallbackExplanationWriter --> FindThatBookTelemetry
    OpenLibraryCatalog --> FindThatBookTelemetry
    Infra --> App_
    Api --> Infra
    Api --> App_
    AiQueryParser --> IAiClient
    AiExplanationWriter --> IAiClient
    GeminiClient -.implements.-> IAiClient
    OpenAiCompatibleClient -.implements.-> IAiClient
    GeminiClient --> GenOptions
    OpenAiCompatibleClient --> GenOptions
    Health --> GenOptions
    OpenLibraryCatalog --> OpenLibraryGate
    OpenLibraryCatalog --> OLPipe
    GeminiClient --> AiPipe
    OpenAiCompatibleClient --> AiPipe
    AiPipe --> ResOptions
    AiPipe --> FindThatBookTelemetry
    OLPipe --> FindThatBookTelemetry
    Health --> ResOptions
```

`FindThatBook.Mcp` uses `FindThatBook.Application` for `ISearchService` and the response types. It uses `FindThatBook.Infrastructure` only to call `AddFindThatBook`, which wires the same catalog, AI clients and decorators as the API. It has no ASP.NET dependency and no settings file.
