# System context

```mermaid
flowchart LR
    user([User in browser])
    subgraph app["Find That Book (one .NET 10 process)"]
        ui["React SPA<br/>static files from wwwroot"]
        api["ASP.NET Core API<br/>POST /api/search<br/>GET /api/health"]
    end
    client([MCP client<br/>Claude Code, Claude Desktop])
    subgraph mcp["FindThatBook.Mcp (separate local .NET process)"]
        tool["stdio MCP server<br/>tool: search_books"]
    end
    ol[("Open Library<br/>search.json, works, authors")]
    covers[("covers.openlibrary.org")]
    ai[("AI provider<br/>Gemini or OpenAI-compatible")]

    user --> ui
    ui -- "fetch /api" --> api
    client -- "JSON-RPC over stdio<br/>(client starts the process)" --> tool
    api -- "HTTP, 2 concurrent, cached,<br/>retry, circuit breaker" --> ol
    api -- "HTTP, structured JSON,<br/>retry, circuit breaker" --> ai
    tool -- "same pipeline and limits,<br/>own cache and circuit state" --> ol
    tool -- "same pipeline,<br/>own circuit state" --> ai
    user -. "cover images" .-> covers
```

The MCP server runs the same search pipeline as the API in its own process. It has no auth, rate limit or HTTP transport, and it is not part of the Docker image. See [03-search-flow.md](03-search-flow.md).
