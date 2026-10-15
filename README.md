# Find That Book

[![CI/CD](https://github.com/juan-carlos-alvarez-ruiz/find-that-book/actions/workflows/ci.yml/badge.svg)](https://github.com/juan-carlos-alvarez-ruiz/find-that-book/actions/workflows/ci.yml)

Turns a messy text query ("tolkien hobbit illustrated deluxe 1937") into up to 5 ranked Open Library matches, each with a one-sentence explanation. .NET 10 Web API, React UI, and an AI provider for query extraction and explanations: Gemini (the brief's default) or any OpenAI-compatible endpoint (Groq, Cerebras, Ollama, OpenRouter, Mistral). See [AI providers](#ai-providers).

## Features

- Accepts sparse, noisy or ambiguous queries: a title only, an author only, both with extra words, partial names ("mark huckleberry"), misspelled or non-English titles.
- Ranks by a deterministic matching hierarchy (primary authors above contributors), so the order is testable and explanations cannot contradict it.
- AI extraction and explanations with automatic fallback to a heuristic parser and template explanations. The app works with no AI key.
- Matches work titles, edition titles (a work titled in another language still matches its English edition), author aliases and transliteration variants ("Dostoevsky" and "Dostoievski").
- Static files and API responses are gzip-compressed for clients that ask for it (the script goes from about 234 KB to 85 KB).
- Gentle with Open Library: capped lookups, a request limit and a cooldown after HTTP 429.
- Health endpoints for liveness, readiness (configuration plus Open Library and AI provider probes) and a masked configuration dump. See [Health endpoints](#health-endpoints).
- UI: the top match shown large with its reason (the headline is the matched edition title when the work is titled differently), covers standing on a wooden ledge on warm paper with serif titles, a logo that links home, the other matches as compact cards with match badges (Best match, Exact title, Author match, Close title) and a collapsible reason, a layout that works on phones, an illustrated progress stage while a search runs (one animated scene per step), dismissable toast messages for errors (with a Try again button), the AI fallback, weak matches and empty results (with an empty bookshelf picture), keyboard and screen reader support (Escape, focused results heading, announced toasts), light/dark/system theme, saved example chips that promote your successful searches and shareable `?q=` links. See [web/README.md](web/README.md).

## Run it

Requirements: .NET 10 SDK, Node 22+.

1. Get an API key. Gemini: https://ai.google.dev/gemini-api/docs/api-key. For an OpenAI-compatible provider such as Groq, see [AI providers](#ai-providers). The app also runs without any key, in fallback mode.
2. Store it. For the default Gemini provider:
   ```bash
   cd src/FindThatBook.Api
   dotnet user-secrets init
   dotnet user-secrets set "Gemini:ApiKey" "<your key>"
   ```
   For an OpenAI-compatible provider, set `Ai__Provider=OpenAiCompatible` and the `OpenAiCompatible__*` variables in your shell (see [AI providers](#ai-providers)). `dotnet run` does not read `.env`.
3. API: `dotnet run --project src/FindThatBook.Api` (http://localhost:5080)
4. UI: `cd web && npm install && npm run dev` (http://localhost:5173, proxies `/api`)
5. Tests: `dotnet test` and `cd web && npm test`

Docker Compose (UI and API on one port, http://localhost:8080):

1. Optional: put your settings in a `.env` file next to `docker-compose.yml` (it is git-ignored, and without a key the app runs in fallback mode):
   ```
   # AI provider: Gemini (default) or OpenAiCompatible
   AI_PROVIDER=Gemini

   # Generation settings sent to either provider. Blank means temperature 0 and nothing else is sent.
   AI_TEMPERATURE=
   AI_TOP_K=
   AI_TOP_P=
   AI_SEED=
   AI_REASONING_EFFORT=

   # Gemini
   GEMINI_API_KEY=<your key>
   GEMINI_MODEL=gemini-3.8-flash

   # OpenAI-compatible provider (here: Groq)
   OPENAI_API_KEY=<your key>
   OPENAI_BASE_URL=https://api.groq.com/openai/v1/
   OPENAI_MODEL=openai/gpt-oss-20b

   # Open Library
   OPENLIBRARY_BASE_URL=https://openlibrary.org/
   OPENLIBRARY_MAX_DETAIL_LOOKUPS=6
   OPENLIBRARY_MAX_AUTHORS_PER_WORK=3
   OPENLIBRARY_MAX_CONCURRENT_REQUESTS=2
   OPENLIBRARY_DEFAULT_COOLDOWN_SECONDS=30
   OPENLIBRARY_CONTACT=<your email or URL>

   # Search limits: per client address per minute, and at once across all clients
   RATE_LIMIT_SEARCHES_PER_MINUTE=10
   RATE_LIMIT_MAX_CONCURRENT_SEARCHES=8

   # Serve GET /api/health/config (and the full /api/health/ready breakdown) to callers on the same machine. Off by default.
   HEALTH_EXPOSE_CONFIG=false
   ```
   Every variable is optional. Blank values use the defaults.
2. Start it: `docker compose up --build`
3. Stop it: `docker compose down`

The compose file also starts a Jaeger all-in-one container (pinned to 1.76.0 by digest, UI at http://localhost:16686, published on `127.0.0.1` only and with no other ports published) for viewing traces. The app only sends to it when telemetry is on and `OTEL_EXPORTER_OTLP_ENDPOINT` points at it, over the compose network. All three containers run with a read-only root file system, all capabilities dropped and `no-new-privileges`. Two optional `.env` values set the search limits: `RATE_LIMIT_SEARCHES_PER_MINUTE` (default 10) and `RATE_LIMIT_MAX_CONCURRENT_SEARCHES` (default 8). The static landing page is served at `/landing.html`.

The compose file also starts a `quick-tunnel` service (`cloudflare/cloudflared` 2026.10.0, pinned by digest, same hardening flags) that publishes the app on a temporary `https://*.trycloudflare.com` URL. Read the URL with `docker compose logs quick-tunnel`. It changes on every start and needs no Cloudflare account or token. Every visitor reaches the app from the tunnel container's address, so all visitors share one search limit (10 per minute), and anyone with the URL can run searches that spend your AI key. Remove the service, or run `docker compose stop quick-tunnel`, when you do not want the app public. A named tunnel with a stable hostname needs a domain in your Cloudflare account and was not set up.

Plain Docker works too:

    docker build -t find-that-book .
    docker run --rm -p 8080:8080 -e Gemini__ApiKey=<your key> find-that-book

Or pull the prebuilt image that CI publishes from `master` (tags `latest` and the commit SHA) and from `v*` tags (tags such as `1.2.3` and `1.2`):

    docker pull ghcr.io/juan-carlos-alvarez-ruiz/find-that-book:latest
    docker run --rm -p 8080:8080 -e Gemini__ApiKey=<your key> ghcr.io/juan-carlos-alvarez-ruiz/find-that-book:latest

## Project layout

```
src/
  FindThatBook.Api/             search and health controllers, problem details, static files, appsettings,
                                OpenTelemetry setup (TelemetryExtensions)
  FindThatBook.Application/     models, interfaces, Normalizer, Matcher, heuristic parser,
                                template explanations, fallback decorators, SearchService,
                                trace source and metrics (Telemetry/)
  FindThatBook.Infrastructure/  Open Library client (+ request gate), AI seam (IAiClient),
                                Gemini and OpenAI-compatible clients, health probes and
                                config check (Health/), HTTP logging and masking (Telemetry/),
                                DI registration
tests/FindThatBook.Tests/       xUnit tests (stub HTTP handlers, fake AI client and catalog)
web/                            React + Vite + TypeScript UI (see web/README.md), plus the
                                static landing page (public/landing.html)
docs/superpowers/               design spec and implementation plan
docs/call-tree-analysis.md      call tree of one traced search (Jaeger spans, HTTP calls, AI timings)
docs/ui-proposals/              three static UI mockups (a featured-result layout based on Bookshelf was implemented)
docs/ui-tests/                  by-hand UI test results, screenshots and pending checks
PRODUCT.md, DESIGN.md           product context and the recorded visual system, for design work
docs/architecture/              Mermaid diagrams: system context, layers, search flow, matching and fallback, infrastructure, Open Library protection
Dockerfile, docker-compose.yml  one image with UI and API
```

Production code under `src/` carries XML documentation comments.

## AI providers

AI is used for two steps: extracting `{ title?, author?, keywords[] }` from the query, and writing one explanation per result. Pick the provider with `Ai:Provider` (env var `Ai__Provider`):

| `Ai:Provider` | Client | Settings (env var form) |
|---|---|---|
| `Gemini` (default) | Gemini `generateContent` | `Gemini__ApiKey`, `Gemini__Model` (default `gemini-3.8-flash`) |
| `OpenAiCompatible` | any `/chat/completions` endpoint | `OpenAiCompatible__ApiKey`, `OpenAiCompatible__BaseUrl` (default `https://api.groq.com/openai/v1/`), `OpenAiCompatible__Model` (default `openai/gpt-oss-20b`) |

A blank model or base URL uses the default, and an unknown provider name stops the app at startup with a clear error. The OpenAI-compatible key is optional because local servers need none. Both AI HTTP calls time out after 30 seconds.

Examples for `OpenAiCompatible`:

| Provider | `BaseUrl` | Example `Model` | Key |
|---|---|---|---|
| Groq | `https://api.groq.com/openai/v1/` | `openai/gpt-oss-20b` | https://console.groq.com/keys |
| Cerebras | `https://api.cerebras.ai/v1/` | see their model list | https://cloud.cerebras.ai |
| Ollama (local) | `http://localhost:11434/v1/` | any pulled model | none |

Model names and free-tier limits change often, so check the provider's own pages. Whichever provider you pick, every AI step still degrades to the non-AI parser and templates on any failure, and the response reports `aiUsed: false`.

How the OpenAI-compatible client works: it posts a `chat/completions` request with `response_format: {"type": "json_object"}` (the mode most providers support), `stream: false` (some gateways stream by default), the configured temperature (0 by default), `top_p` and `top_k` when they are set, puts the expected JSON Schema and our instructions in the system message and only the untrusted data (the reader's text or the catalog records) in the user message, and wraps array-shaped results in a `{"result": ...}` object because JSON mode needs an object at the root. The reply is parsed and validated by the same code as for Gemini. Schemas are written once as standard JSON Schema, and the Gemini client converts them to Gemini's dialect.

### Generation settings: temperature, top-k, top-p, seed and reasoning

Both providers take the same five values from the `Ai` configuration section (env vars `Ai__Temperature`, `Ai__TopK`, `Ai__TopP`, `Ai__Seed` and `Ai__ReasoningEffort`, or `AI_TEMPERATURE`, `AI_TOP_K`, `AI_TOP_P`, `AI_SEED` and `AI_REASONING_EFFORT` for Docker Compose):

| Setting | Range | Not set | Gemini field | OpenAI-compatible field |
|---|---|---|---|---|
| `Ai:Temperature` | 0 to 2 | 0 | `generationConfig.temperature` | `temperature` |
| `Ai:TopK` | 1 or more | not sent, the provider's default applies | `generationConfig.topK` | `top_k` |
| `Ai:TopP` | above 0, at most 1 | not sent, the provider's default applies | `generationConfig.topP` | `top_p` |
| `Ai:Seed` | any integer | not sent | `generationConfig.seed` | `seed` |
| `Ai:ReasoningEffort` | a short word such as `minimal`, `low`, `medium` or `high` | not sent, the model's default applies | `generationConfig.thinkingConfig.thinkingLevel` | `reasoning_effort` |

A blank value counts as not set. An out-of-range value is not sent (the temperature falls back to 0), and `/api/health/ready` reports the setting as invalid (the configuration check is `down`, so the app is `unready`). The messages name the setting and never print its value. `GET /api/health/config` shows the values that are sent. `top_k` is not part of the OpenAI chat API, and some gateways reject unknown parameters, so leave `AI_TOP_K` blank for those. Ranking never depends on these values, and every AI step still falls back to the non-AI version on any failure.

The seed asks the provider to repeat its choices for the same input, as far as it supports that, and it does not guarantee identical text. The accepted reasoning words depend on the model (providers use `minimal`, `low`, `medium`, `high` and, on some models, `none` or `xhigh`), so only the shape is checked: a lowercase word of letters, digits, hyphens or underscores, starting with a letter, up to 20 characters. The value is trimmed and lowercased. A model that does not support a reasoning setting rejects it with an error, and then every search falls back to basic parsing. A third-party report said xAI's Grok models reject `reasoning_effort`. That did not hold when tried: on 2026-10-14 `api.x.ai` accepted `reasoning_effort: "low"`, `seed` and `top_p` for `grok-4.7` (HTTP 200, see `docs/call-tree-analysis.md`). It cut the hidden reasoning tokens from 467 and 1,370 to 103 and 180 in that one sample. The seed did not make the text repeat there. Whether another model or gateway accepts these values still has to be tried, so leave them blank until you have. Gemini's older `thinkingBudget` is not offered.

### Why Gemini alone is not reliable enough

We built the OpenAI-compatible option because Gemini's free tier was unreliable when we tested it. Observations from 2026-10-08 with one free-tier key (these describe that key and day, not Gemini in general):

- The original default `gemini-2.5-flash` returned 404: "no longer available to new users". Google's pricing page still listed it, so a default that looks valid can stop working without notice.
- `gemini-3.8-flash`, `gemini-3.7-flash`, `gemini-3.6-flash`, `gemini-3.5-flash-lite`, `gemini-3.1-flash-lite` and `gemini-flash-latest` returned 503 "high demand" on repeated attempts over a long session. The app also received 429 rate limits.
- `gemini-3.5-flash` answered but took about 22 seconds for a single extraction.
- Result: most requests fell back to the heuristic parser, which works but is less precise (for example "austen bennet" does not find Pride and Prejudice).

The fallback keeps the app usable, but it hides the problem from users, so having a second, selectable provider is the practical fix. Switching is configuration only, with no code change.

### Choosing a model on a routing gateway

Observations from 2026-10-09 with one free routing gateway (they describe that gateway and day, not the models in general):

- With `OpenAiCompatible__Model=auto` the gateway picks a different upstream model per call. A one-line extraction took 6.6 to 12.2 seconds over three runs, and the log showed `AI provider timed out` (the 30 second limit) on some searches, so searches fell back with `aiUsed: false` while `/api/health/ready` still said `ready`.
- 20 models were timed once each on the explanation step (5 books, JSON mode). 11 returned the exact 5-item JSON the app needs, 6 returned output it cannot use (wrong shape or count), and 3 returned HTTP errors (429, 400, 502). The fastest valid ones took under 4 seconds (`command-a` 1.9 s, `nemotron-3-super-120b` 3.7 s, `gpt-oss-20b` 3.9 s). Some valid ones took 20 to 28 seconds, close to the timeout.
- Each model ran once, so repeat the test before relying on the numbers. The project owner runs with `command-a`.

## API

`POST /api/search` with `{ "query": "..." }` returns `{ aiUsed, message, results: [{ title, author, authors, first_publish_year, workId, url, coverUrl, explanation, matchTier, exactTitle, matchedTitle }] }`. `matchTier` is 1 (strongest) to 5, `exactTitle` is true when the title matched exactly and `matchedTitle` is the edition title that matched when the work is titled differently (otherwise null). The query is trimmed and truncated to 200 characters. `message` is also set next to non-empty results when even the best match is weak ("No confident match").
Errors are RFC 7807 problem details: 400 for a blank query, 413 for a request body over 8 KB, 429 when the client or the server is over the search limit (with a `Retry-After` header, see [Security](#security)), 502 if Open Library cannot be searched, 500 for anything else.

### Health endpoints

| Endpoint | Purpose | Response |
| --- | --- | --- |
| `GET /api/health` | Liveness for container and load balancer checks. No outside calls. | 200 `{ "status": "ok" }` |
| `GET /api/health/ready` | Readiness. Validates the configuration and probes Open Library and the selected AI provider. | 200 with `ready` or `degraded`, 503 with `unready`. The body is only `{ "status": ... }`. The per-dependency breakdown shows only to a local caller when `Health:ExposeConfig` is true. |
| `GET /api/health/config` | The effective configuration values, with masked API keys. Off (404) unless `Health:ExposeConfig` is true. The `ai` section shows the provider and the generation values that are sent (temperature, top-k, top-p, seed, reasoning effort). | 200 with `ai`, `gemini`, `openAiCompatible` and `openLibrary` sections |

- **Probes:** Open Library runs a one-result search. Gemini reads the configured model's metadata and OpenAI-compatible calls `GET {baseUrl}/models`, so neither spends tokens. A server without a `/models` route still counts as reachable. Each probe times out after 5 seconds.
- **Status:** `unready` means the configuration is invalid or Open Library is down. `degraded` means the AI provider is missing a key, rate limited or down, and searches use the non-AI fallback.
- **Caching:** the readiness report is cached for 30 seconds, so polling does not use AI quota or trip Open Library throttling.
- **Secrets:** `/api/health/config` masks API keys (only the last four characters stay visible, for example `****...****654c`, and keys shorter than 16 characters are fully masked), shows `not set` for a missing key and removes credentials from URLs. The endpoint is off unless `Health:ExposeConfig` is true (`Health__ExposeConfig`, or `HEALTH_EXPOSE_CONFIG` for Docker Compose), because a reverse proxy on the same machine that adds no forwarding header would make every outside caller look local. When it is on, it answers only callers on the same machine (loopback, or a remote address equal to the local one) and returns 404 to everyone else, including a caller whose address is unknown and any request that carries `X-Forwarded-For`, `Forwarded` or `X-Real-IP` (a proxy on the same machine would otherwise make every outside caller look local), because it shows internal URLs and the Open Library contact. Under Docker, requests through a published port come from the bridge gateway, so run `curl http://localhost:8080/api/health/config` inside the container instead.
- **Controller:** the three endpoints live in `HealthController`. Settings come from the app's configuration, not from `.env`, which only Docker Compose reads.

## Security

- **Rate limit:** `POST /api/search` allows 10 searches per minute per client address and 8 searches at once across all clients (`RateLimit:SearchPermitsPerMinute`, `RateLimit:MaxConcurrentSearches`, env vars `RateLimit__*`). Each search can spend two AI calls and a dozen Open Library requests, and one client could otherwise start the shared Open Library cooldown for everyone. Other paths are not limited. Trailing slashes are ignored, so `/api/search/` counts against the same limits as `/api/search`. The client address is the connection's remote address, so behind a reverse proxy all readers share one limit until forwarded headers are configured with known proxies.
- **Request size:** Kestrel rejects bodies over 8 KB with a 413.
- **Browser headers:** every response, including the 500, 502 and 413 problem details (the headers are set when the response starts, because the exception handler clears earlier ones), carries `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy: no-referrer`, a `Permissions-Policy` that turns off camera, microphone, location, payment and USB, and a Content-Security-Policy that allows only the app's own scripts and styles, and images from `covers.openlibrary.org` and `archive.org` (cover redirects). The theme script is a file (`web/public/theme-init.js`) so no inline script is needed. The static landing page has its own policy that allows its inline script and styles. HSTS and HTTPS redirection are left to the proxy that terminates TLS.
- **Prompt injection:** the model has no tools, no secrets in its prompt and no say in the ranking, so injected text can only change wording. Instructions and data travel in separate roles (`IAiClient` takes an `AiPrompt` with `Instructions` and `Data`): Gemini gets them as `systemInstruction` and user content, and OpenAI-compatible endpoints as the system and user messages. Two inputs are untrusted. The reader's text goes into the parser prompt with `<` and `>` removed so it cannot close the delimiter. The book facts (titles, authors and contributors, which come from Open Library where anyone can edit records) go into the explanation prompt after control characters, angle brackets and backticks are removed and each value is cut (titles 200 characters, names 100, at most 5 authors and 5 contributors). They sit in a block marked as untrusted data, with a delimiter that is random for each request.
- **AI output is untrusted:** the explanation reply must be an array with exactly one entry per book, with no duplicate, missing or out-of-range index. Each explanation is checked against the facts of its own book (`ExplanationGuard`): at most 240 characters and 40 words, no link, no email or domain written out (`dot com`, `[.]`), no number that is not in the facts, no capitalised word that is not in the facts, none of a few instruction or promotion words (`best`, `ignore`, `visit` and others), no "exact" for a near match and no contributor when the book has none. A word that is part of a real title is allowed. Any failure falls back to template explanations with `aiUsed: false`. The parser reply is limited too: a title or author over 200 characters or with a control character is rejected, and keywords are cut to 10 of at most 50 characters.
- **Response size:** every outgoing HTTP response is capped at 2 MB (`ServiceCollectionExtensions.MaxResponseBytes`), including when HTTP body logging buffers it, so an oversized upstream reply fails the request instead of filling memory.
- **Plain http to an AI gateway:** readiness reports `degraded` when an OpenAI-compatible API key would be sent over `http` to a host other than loopback. A LAN gateway still works.
- **Open Library ids:** only keys shaped like `/works/OL123W` and `/authors/OL123A` are followed, so a malformed upstream value cannot change the path of a later request.
- **Supply chain:** Dockerfile base images are pinned by digest and the workflow actions by commit SHA (Dependabot updates both, and the compose images too). `npm ci --ignore-scripts` is used in the Dockerfile and CI, so dependency install scripts never run. CI fails on a known vulnerable NuGet package (`dotnet list package --vulnerable --include-transitive`) or an npm advisory of high severity or above (`npm audit --audit-level=high`). `.dockerignore` keeps `.env` and `.github` out of the build context.

## Telemetry

The API exports traces, metrics and logs over OTLP. It is off unless `Telemetry__Enabled=true` or `OTEL_EXPORTER_OTLP_ENDPOINT` is set (`TELEMETRY_ENABLED` and `OTEL_*` for Docker Compose). `OTEL_EXPORTER_OTLP_HEADERS`, `OTEL_EXPORTER_OTLP_PROTOCOL` and `OTEL_SERVICE_NAME` are read by the OpenTelemetry SDK.

- Traces: ASP.NET Core, outgoing HTTP, and a `search` span with `parse`, `catalog`, `rank` and `explain` children.
- Metrics: ASP.NET Core, HTTP client, runtime, plus `ftb.ai.fallback` (tag `step`), `ftb.openlibrary.cooldown` and `ftb.search.results`.
- Logs: every `ILogger` entry, plus each outgoing HTTP request and response with headers and bodies (cut at 8 KB). Set `Telemetry__LogHttpBodies=false` to turn the HTTP bodies off.
- Masking: `Authorization`, `x-goog-api-key` and cookie headers, credential query parameters and JSON fields, bearer tokens, the configured API keys and the Open Library contact are replaced with `***`. The reader's search text is logged as typed.

## How it works

1. **Parse.** The AI provider turns the text into `{ title?, author?, keywords[] }`, one hypothesis. Without AI, a heuristic parser strips noise words and years into keywords and emits up to six guessed hypotheses (whole text as title, as author, first or last word as author, first or last two words as author).
2. **Search.** `OpenLibraryCatalog` searches every hypothesis in parallel, asking Open Library for matching edition titles too. When a fielded title/author search finds fewer than three works, a free-text search is added, so works titled in another language are still found. Works are then looked up (at most 6 per search) to find their primary authors, with each author's alternate names kept for matching. Authors listed in search results but not on the work record are contributors.
3. **Match and rank** (pure code, `Normalizer` and `Matcher`). Text is lowercased, with diacritics, apostrophes, punctuation, subtitles and leading articles removed and initials merged. A title matches exactly or nearly against the work title or an edition title. An author matches when every query word equals or starts a name word, through aliases and transliteration variants. Tiers:
   1. exact title + primary author
   2. exact title + contributor-only author
   3. near title + author
   4. author only (top works by editions)
   5. title match without a matching author

   Results sort by strength: tiers 1 and 2, tier 3 from an AI-extracted author, tier 4, an exact title alone, tier 3 from a guessed author, then a near title alone. Ties break on a matching year keyword, then edition count. Works are deduplicated by full title and first primary author, summaries and study guides are dropped (unless the query asks for one), and the top 5 are kept. When the best result is a guessed author split with a near title, or a near title with no author, the response carries a "No confident match" message.
4. **Explain.** The AI provider writes one sentence per result from fetched facts only (the publication year is given only when it matched the query, so it cannot be misstated). Template explanations are the fallback.

## Decisions and assumptions

- Ranking is deterministic so the order is testable and explanations cannot contradict it. LLM re-ranking is left out.
- Every AI step degrades to a non-AI version and `aiUsed` becomes `false` (missing key, rate limit, timeout, malformed JSON).
- Tier 5 was added for title-only queries, which the brief's four tiers do not cover.
- A guessed author split ranks below an exact title on its own, so "the little prince" finds the book before a "Little Pigs" book by an author named Prince.
- Author-only queries use `/search.json?sort=editions` instead of `/authors/{id}/works.json`, which has no popularity order.
- Open Library data quirks are handled in matching: a work can be titled in another language ("海辺のカフカ") while its English edition carries the title the reader typed, and authors can be stored in another script. Names are shown as Open Library stores them. When a title matched through an edition title, the UI shows that edition title as the headline and the stored work title beneath it ("Catalogued as ...").
- Open Library calls are cached in memory for an hour, concurrent calls for the same URL share one request that no single caller's cancellation can cancel (a caller that disconnects stops waiting and the others still get the answer), and calls are retried twice on 5xx and timeouts. A failed detail lookup lowers a candidate's rank instead of failing the request.
- The app is gentle with Open Library, which rate-limits (HTTP 429) heavy use. Each search looks up details for at most 6 works and 3 authors per work (`OpenLibrary:MaxDetailLookups`, `MaxAuthorsPerWork`), and at most 2 requests run at the same time (`MaxConcurrentRequests`). A 429 is never retried: it pauses all Open Library requests for the `Retry-After` time, or 30 s (`DefaultCooldownSeconds`), and during that pause searches fail fast with a 502 while cached lookups still work.
- Open Library asks API users to identify themselves. Set `OpenLibrary__Contact` (an email or URL, or `OPENLIBRARY_CONTACT` for Docker Compose) and it is added to the `User-Agent`. It is empty by default and never filled in automatically.
- User text goes to the AI provider as delimited data, and only the structured fields it returns are used.

## Known limitations

- Authors stored in another script show in that script (for example 村上春樹), even when the title headline is the English edition title.
- Pressing Enter while a search is running does nothing, because the search box, the example chips and the Search button are disabled during a search.
- When the AI provider hangs, a search can wait up to about 40 seconds before falling back (two AI steps at 30 seconds each).
- In fallback mode the guessed hypotheses can still produce odd results. Summaries and study guides are filtered by title phrase (summary, study guide, SparkNotes, CliffsNotes), so ones with other wording can still appear.
- Different first-name spellings (for example Fyodor and Fedor) only match when the surname also matches.
- The mobile layout was checked by hand in a narrow desktop Chrome window, not on real phones, and there are no automated layout tests.
- The readiness probes were run live against Open Library and an OpenAI-compatible endpoint, but the Gemini probe was not run with a real key.
- The AI readiness probes only list models. A gateway that is slow or stalls on chat completions still reports `ready`, and its searches fall back after the 30 second timeout.
- With no results the only message is a toast, so dismissing it leaves the page blank for that search.
- The toasts were checked by hand in headed Chrome with stubbed responses (see `docs/ui-tests/README.md`), not with a screen reader or on a real phone. The extra bottom padding that keeps the last result clear of the toasts uses `:has()`, so a browser without it can show a toast over the last result.
- The search limit counts by the connection's remote address, so behind a reverse proxy every reader shares it. Forwarded headers need configuring with known proxies first. The limit state lives in memory, so it is per instance and resets on restart.
- The security headers were checked by hand against the Docker image started with `docker compose up` (so with the compose hardening flags): on 2026-10-13 in headless Chrome and on 2026-10-14 in headed Chrome (T30 in `docs/ui-tests/README.md`). The page, a search and the covers load with no policy violation logged, and a browser refuses to show the app in a frame (`X-Frame-Options: DENY` and `frame-ancestors 'none'` are enforced). No automated browser test covers the Content-Security-Policy.
- `AllowedHosts` is `*`, so the app answers any `Host` header. Set it to your hostnames when you deploy. The Content-Security-Policy keeps `style-src-attr 'unsafe-inline'` (the progress scenes use inline style attributes) and allows `https://*.archive.org` (cover redirects).
- A shared Open Library request still runs to the end when every caller has cancelled. It is bounded by the 10 second timeout and the retry limit, and its result lands in the cache.
- The image has no `HEALTHCHECK` (the base image has no `curl` or `wget`) and CI does not scan the built image for vulnerabilities.
- The Docker image builds and serves the UI and API (checked by hand on 2026-10-10, 2026-10-13 and 2026-10-14, see `docs/ui-tests/README.md`), AI mode was checked once with a real gateway (T32: `aiUsed: true`, no fallback toast), and the CI publish job builds the image and smoke-tests it (`/api/health` and the UI root) before pushing, but no test in the repo exercises searches against the running image.

## Testing

- 487 .NET tests (generation-setting tests cover the defaults, range and shape checks, both providers' request bodies, configuration binding through the real container, the readiness check and the config dump; security tests cover prompt injection, role separation, the rate limit (including path variants), the opt-in and proxy-aware config endpoint, the minimal readiness body, the 413 response, the headers (including on error responses), the response size cap, shared requests and cancellation, id validation, key masking, AI output limits and the pinned compose, Dockerfile and workflow files). `Normalizer` and `Matcher` carry most coverage (every tier, contributors, subtitles, diacritics, apostrophes, initials, transliteration, dedupe, ties, guessed versus extracted authors, edition titles, aliases).
- Parser, writer and client tests use a fake AI client and stub `HttpMessageHandler`s, including malformed JSON, HTTP errors, schema conversion and provider selection.
- Open Library catalog tests cover parsing, retries, enrichment failures, lookup caps, shared lookups for an author used by several works, the concurrency limit and the 429 cooldown.
- Integration tests use `WebApplicationFactory` with a fake catalog, covering fallback mode, AI mode, 400, 502 and empty queries.
- Telemetry tests cover the masker, the HTTP logging handler (bodies, truncation, no credentials in the log), the search spans, the three custom metrics and the on/off wiring. They run against in-process listeners, never a collector. The OTLP export itself was not run against a real collector.
- 88 frontend tests (Vitest + Testing Library) cover results, match badges, the reason toggle and Escape, the focused results heading, the toasts (error, AI notice, weak match and empty state, dismissing one and its return after a new search), Try again, stale responses, the progress stage (each step with its own duration), the disabled search box and example chips during a search, the empty bookshelf, the compact hero, the theme toggle, the example list (including saving it) and the shareable `?q=` link.
- Health tests cover each probe (ok, failure statuses, timeout, unreachable host, 429 cooldown), configuration validation, the overall status rules, the 30-second cache, the 200 and 503 responses and the masked configuration dump.
- No test calls a live API.

## Docs

- Design: [docs/superpowers/specs/2026-10-08-find-that-book-design.md](docs/superpowers/specs/2026-10-08-find-that-book-design.md)
- Implementation plan (as built): [docs/superpowers/plans/2026-10-08-find-that-book.md](docs/superpowers/plans/2026-10-08-find-that-book.md)
- Decision record (ideas set aside, changes from the first draft): [docs/decisions.md](docs/decisions.md)
- Architecture diagrams (one file per diagram): [docs/architecture/](docs/architecture/)
- Call tree of one traced search: [docs/call-tree-analysis.md](docs/call-tree-analysis.md)
- Product context: [PRODUCT.md](PRODUCT.md). Visual system: [DESIGN.md](DESIGN.md).

## With more time

Fail faster when Open Library does not answer, let a new search replace a running one, sort title-only searches by edition count, show an English display name for authors stored in another script, LLM re-ranking behind a flag, a persistent cache, ISBN matching, query logging for tuning, and a hosted live demo.
