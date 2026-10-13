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

    subgraph Infra["FindThatBook.Infrastructure"]
        OpenLibraryCatalog
        OpenLibraryGate
        AiQueryParser
        AiExplanationWriter
        IAiClient{{IAiClient}}
        GeminiClient
        OpenAiCompatibleClient
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
    OpenLibraryCatalog --> OpenLibraryGate
```
