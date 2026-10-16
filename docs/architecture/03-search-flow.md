# Search request flow

```mermaid
sequenceDiagram
    actor U as User
    participant W as React SPA
    participant C as SearchController
    participant S as SearchService
    participant P as FallbackQueryParser
    participant A as AI client
    participant H as HeuristicQueryParser
    participant K as OpenLibraryCatalog
    participant O as Open Library
    participant M as Matcher
    participant E as FallbackExplanationWriter

    U->>W: messy query
    W->>C: POST /api/search
    C->>S: SearchAsync(query)
    S->>P: ParseAsync
    Note over P,A: AiResiliencePipeline: retry once, circuit breaker, timeouts (08)
    P->>A: extract title/author hypotheses
    alt AiUnavailableException (error, timeout or open circuit)
        P->>H: parse locally
        H-->>P: hypotheses, aiUsed=false
    else ok
        A-->>P: hypotheses, aiUsed=true
    end
    P-->>S: ParseOutcome
    S->>K: FindAsync(hypotheses)
    Note over K,O: OpenLibraryResiliencePipeline: retry, circuit breaker, timeout per attempt (06)
    K->>O: search.json
    K->>O: works and authors (max 6 lookups, cached)
    K-->>S: candidates
    S->>M: Rank(hypotheses, candidates, 5)
    M-->>S: up to 5 matches by tier
    S->>E: WriteAsync(matches)
    Note over E,A: same AiResiliencePipeline and circuit as the parse step
    E->>A: one sentence per match
    alt AiUnavailableException (error, timeout or open circuit)
        E-->>S: template sentences, aiUsed=false
    else ok
        A-->>E: sentences
        E-->>S: sentences, aiUsed=true
    end
    S-->>C: SearchResponse
    C-->>W: JSON (aiUsed, message, results)
    W-->>U: ranked result cards
```

## Search through the MCP server

The MCP tool enters the same pipeline at `SearchService`. Everything after `SearchAsync` in the diagram above is unchanged.

```mermaid
sequenceDiagram
    actor C as MCP client
    participant T as BookSearchTools
    participant S as SearchService

    C->>T: tools/call search_books(query) over stdio
    alt blank query
        T-->>C: error result, empty-query message (search never called)
    else
        T->>S: SearchAsync(query)
        alt CatalogUnavailableException
            S-->>T: exception
            T-->>C: error result, "Open Library is unavailable or cooling down"
        else unexpected exception
            S-->>T: exception
            T-->>C: generic tool error, detail logged to stderr
        else ok
            S-->>T: SearchResponse (aiUsed, message, results)
            T-->>C: the response as JSON text (first_publish_year in snake_case)
        end
    end
```

`AiUnavailableException` never reaches the tool. The fallback decorators handle it and the response carries `aiUsed: false`.
