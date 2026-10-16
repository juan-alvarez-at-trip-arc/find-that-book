# Telemetry

Off unless `Api:Telemetry:Enabled` is true or `OTEL_EXPORTER_OTLP_ENDPOINT` is set.

```mermaid
flowchart LR
    subgraph Api["FindThatBook.Api"]
        TE["TelemetryExtensions<br/>AddFindThatBookTelemetry"]
        OTel["OpenTelemetry SDK<br/>traces, metrics, logs"]
    end

    subgraph App_["FindThatBook.Application"]
        FT["FindThatBookTelemetry<br/>ActivitySource and Meter"]
        SS["SearchService<br/>spans: search, parse, catalog, rank, explain"]
        FB["Fallback decorators<br/>ftb.ai.fallback"]
        SR["ftb.search.results"]
    end

    subgraph Infra["FindThatBook.Infrastructure"]
        HL["HttpLoggingHandler"]
        M["SensitiveDataMasker"]
        OC["OpenLibraryCatalog<br/>ftb.openlibrary.cooldown"]
        RP["OpenLibraryResiliencePipeline, AiResiliencePipeline<br/>ftb.openlibrary.circuit, ftb.ai.circuit<br/>(breaker opened and closed callbacks)"]
    end

    SS --> FT
    FB --> FT
    SS --> SR --> FT
    OC --> FT
    RP --> FT
    TE --> OTel
    FT -- "source and meter" --> OTel
    HL --> M
    HL -- "ILogger entries" --> OTel
    TE -- "Api:Telemetry:LogHttpBodies" --> HL
    OTel -- "OTLP" --> col[(OTLP collector)]
    HL -. "every HttpClient" .-> ext[(Open Library, AI provider)]
```

The MCP server (`FindThatBook.Mcp`) registers no telemetry and no HTTP body logging, because those are set up by `TelemetryExtensions` in the API, not by `AddFindThatBook`. Its logs go to stderr only.

`HttpLoggingHandler` masks first and truncates second (8 KB), so a secret cut in half is never logged.

The circuit counters (`ftb.ai.circuit` and `ftb.openlibrary.circuit`, tag `state` with `opened` or `closed`) are recorded from the circuit breaker callbacks of the two Polly pipelines, through `FindThatBookTelemetry`. The Polly meter itself is not subscribed.
