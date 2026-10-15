# Open Library protection

Every attempt to reach Open Library runs through `OpenLibraryResiliencePipeline` (Polly, one instance for the process, so the circuit state is shared). Strategies, outermost first: retry, circuit breaker, timeout per attempt.

```mermaid
flowchart LR
    req[Catalog call] --> cache{"In-memory cache<br/>1 hour, size limit 2000"}
    cache -- hit --> ret[Return]
    cache -- miss --> infl{"Same URL already<br/>in flight?"}
    infl -- yes --> join["Wait for that request<br/>(own cancellation only)"] --> ret
    infl -- no --> retry

    subgraph pipe["OpenLibraryResiliencePipeline (Polly)"]
        retry["Retry: Resilience MaxRetries times<br/>growing delay with jitter<br/>on 5xx, network error, timeout, bad JSON"] --> cb{"Circuit open?"}
        cb -- no --> tmo["Timeout per attempt<br/>10 s (AttemptTimeoutMs)"]
    end

    cb -- "yes: BrokenCircuitException" --> fail[CatalogUnavailableException]
    tmo --> cd{Cooldown active?}
    cd -- yes --> fail
    cd -- no --> gate["OpenLibraryGate<br/>2 concurrent requests"]
    gate --> http[HTTP GET]
    http -- 429 --> start["Start cooldown, no retry,<br/>not counted by the circuit"] --> fail
    http -- "5xx, timeout, bad JSON" --> retry
    http -- other 4xx --> fail
    http -- 404 --> ret
    http -- 200 --> store[Cache result] --> ret
```

The circuit opens when at least half of the requests in 30 seconds failed, with at least 6 requests (`OpenLibrary:Resilience:CircuitBreaker*`). It stays open for 20 seconds, then lets one request through. While it is open, searches fail with a 502 and send no request. Opening and closing are counted by `ftb.openlibrary.circuit`.
