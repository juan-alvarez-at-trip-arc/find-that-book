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
        env[".env (git-ignored)<br/>AI_PROVIDER, keys, models,<br/>OPENLIBRARY_*, TELEMETRY_*, OTEL_*"]
        ctr["container: find-that-book<br/>port 8080, restart unless-stopped"]
        jg["container: jaeger 1.76.0 (pinned by digest)<br/>UI on 127.0.0.1:16686 only"]
        env --> ctr
    end

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

`dotnet run` reads env vars, not `.env`. See [07-telemetry.md](07-telemetry.md) for what is exported. Only `docker compose` reads `.env`.
