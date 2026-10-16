# Infrastructure and deployment

## Docker

```mermaid
flowchart TB
    subgraph build["Dockerfile (multi-stage)"]
        s1["node:22-alpine<br/>npm ci, npm run build"]
        s2["dotnet/sdk:10.0<br/>dotnet publish Api"]
        s3["dotnet/aspnet:10.0<br/>app + wwwroot"]
        s1 -- "dist to wwwroot" --> s3
        s2 -- "publish output" --> s3
    end

    subgraph compose["docker compose"]
        env[".env (git-ignored)<br/>AI_PROVIDER, AI_MODEL, AI_*_API_KEY,<br/>AI_TEMPERATURE, AI_TOP_K, AI_TOP_P,<br/>AI_SEED, AI_REASONING_EFFORT,<br/>AI_RESILIENCE_*,<br/>API_HEALTH_EXPOSE_CONFIG, API_RATE_LIMIT_*,<br/>OPEN_LIBRARY_*, API_TELEMETRY_*, OTEL_*"]
        ctr["container: find-that-book<br/>port 8080, restart unless-stopped"]
        jg["container: jaeger 1.76.0 (pinned by digest)<br/>UI on 127.0.0.1:16686 only"]
        qt["container: quick-tunnel<br/>cloudflared 2026.10.0 (pinned by digest)<br/>temporary trycloudflare.com URL"]
        env --> ctr
        qt -- "http://app:8080" --> ctr
    end
    qt -- "outbound QUIC" --> cf[(Cloudflare edge)]
    web([Internet visitors]) -- "https://*.trycloudflare.com" --> cf

    s3 --> ctr
    ctr -- "HTTPS" --> ol[(openlibrary.org)]
    ctr -- "HTTPS" --> gem[(Gemini API)]
    ctr -- "HTTPS" --> oai[("OpenAI-compatible gateway<br/>default: Groq")]
    ctr -. "OTLP, only when telemetry is on" .-> otlp[(OTLP collector)]
    otlp -.-> jg
    host([Host browser]) -- "http://localhost:8080" --> ctr
```

## Local development

```mermaid
flowchart LR
    b([Browser :5173]) --> vite["Vite dev server<br/>proxies /api"]
    vite --> api["dotnet run Api :5080"]
    api --> ext[(Open Library, AI provider)]
```

## MCP server

```mermaid
flowchart LR
    cl(["MCP client<br/>Claude Code, Claude Desktop"]) -- "starts the process,<br/>JSON-RPC on stdin and stdout" --> mcp["dotnet FindThatBook.Mcp.dll<br/>(built with dotnet build -c Release)"]
    cfg["env vars in the client's MCP config<br/>Ai__*, OpenLibrary__Contact, ..."] --> mcp
    mcp -- "logs on stderr only" --> cl
    mcp --> ext[(Open Library, AI provider)]
```

The MCP server is not in the Docker image or `docker-compose.yml`, which publish only the API. It reads environment variables, not `.env`, user secrets or an `appsettings.json` (the host's content root is its own folder). Start it from a built dll, not `dotnet run`, and use an absolute path when the client does not start it from the repository root.

`dotnet run` reads env vars, not `.env`. See [07-telemetry.md](07-telemetry.md) for what is exported. Only `docker compose` reads `.env`.
