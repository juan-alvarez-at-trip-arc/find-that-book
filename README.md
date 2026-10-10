# Find That Book

Turns a messy text query ("tolkien hobbit illustrated deluxe 1937") into up to 5 ranked Open Library matches, each with a one-sentence explanation. .NET 8 Web API, React UI, and an AI provider for query extraction and explanations: Gemini (the brief's default) or any OpenAI-compatible endpoint (Groq, Cerebras, Ollama, OpenRouter, Mistral). See [AI providers](#ai-providers).

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

Requirements: .NET 8 SDK, Node 22+.

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
   ```
   Every variable is optional. Blank values use the defaults.
2. Start it: `docker compose up --build`
3. Stop it: `docker compose down`

Plain Docker works too:

    docker build -t find-that-book .
    docker run --rm -p 8080:8080 -e Gemini__ApiKey=<your key> find-that-book

## Project layout

```
src/
  FindThatBook.Api/             search and health controllers, problem details, static files, appsettings
  FindThatBook.Application/     models, interfaces, Normalizer, Matcher, heuristic parser,
                                template explanations, fallback decorators, SearchService
  FindThatBook.Infrastructure/  Open Library client (+ request gate), AI seam (IAiClient),
                                Gemini and OpenAI-compatible clients, health probes and
                                config check (Health/), DI registration
tests/FindThatBook.Tests/       xUnit tests (stub HTTP handlers, fake AI client and catalog)
web/                            React + Vite + TypeScript UI (see web/README.md)
docs/superpowers/               design spec and implementation plan
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

How the OpenAI-compatible client works: it posts a `chat/completions` request with `response_format: {"type": "json_object"}` (the mode most providers support), `stream: false` (some gateways stream by default), temperature 0, puts the expected JSON Schema in the system message, and wraps array-shaped results in a `{"result": ...}` object because JSON mode needs an object at the root. The reply is parsed and validated by the same code as for Gemini. Schemas are written once as standard JSON Schema, and the Gemini client converts them to Gemini's dialect.

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
Errors are RFC 7807 problem details: 400 for a blank query, 502 if Open Library cannot be searched, 500 for anything else.

### Health endpoints

| Endpoint | Purpose | Response |
| --- | --- | --- |
| `GET /api/health` | Liveness for container and load balancer checks. No outside calls. | 200 `{ "status": "ok" }` |
| `GET /api/health/ready` | Readiness. Validates the configuration and probes Open Library and the selected AI provider. | 200 with `ready` or `degraded`, 503 with `unready` |
| `GET /api/health/config` | The effective configuration values, with masked API keys. | 200 with `ai`, `gemini`, `openAiCompatible` and `openLibrary` sections |

- **Probes:** Open Library runs a one-result search. Gemini reads the configured model's metadata and OpenAI-compatible calls `GET {baseUrl}/models`, so neither spends tokens. A server without a `/models` route still counts as reachable. Each probe times out after 5 seconds.
- **Status:** `unready` means the configuration is invalid or Open Library is down. `degraded` means the AI provider is missing a key, rate limited or down, and searches use the non-AI fallback.
- **Caching:** the readiness report is cached for 30 seconds, so polling does not use AI quota or trip Open Library throttling.
- **Secrets:** `/api/health/config` masks API keys (first and last four characters stay visible, for example `free****...****654c`, and keys shorter than 12 characters are fully masked), shows `not set` for a missing key and removes credentials from URLs. The endpoint answers only callers on the same machine (loopback, or a remote address equal to the local one) and returns 404 to everyone else, because it shows internal URLs and the Open Library contact. Under Docker, requests through a published port come from the bridge gateway, so run `curl http://localhost:8080/api/health/config` inside the container instead.
- **Controller:** the three endpoints live in `HealthController`. Settings come from the app's configuration, not from `.env`, which only Docker Compose reads.

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
- Open Library calls are cached in memory for an hour and retried twice on 5xx and timeouts. A failed detail lookup lowers a candidate's rank instead of failing the request.
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
- The Docker image builds and serves the UI and API (checked by hand on 2026-10-10, see `docs/ui-tests/README.md`), but no automated test builds or runs the image.

## Testing

- 291 .NET tests. `Normalizer` and `Matcher` carry most coverage (every tier, contributors, subtitles, diacritics, apostrophes, initials, transliteration, dedupe, ties, guessed versus extracted authors, edition titles, aliases).
- Parser, writer and client tests use a fake AI client and stub `HttpMessageHandler`s, including malformed JSON, HTTP errors, schema conversion and provider selection.
- Open Library catalog tests cover parsing, retries, enrichment failures, lookup caps, the concurrency limit and the 429 cooldown.
- Integration tests use `WebApplicationFactory` with a fake catalog, covering fallback mode, AI mode, 400, 502 and empty queries.
- 88 frontend tests (Vitest + Testing Library) cover results, match badges, the reason toggle and Escape, the focused results heading, the toasts (error, AI notice, weak match and empty state, dismissing one and its return after a new search), Try again, stale responses, the progress stage (each step with its own duration), the disabled search box and example chips during a search, the empty bookshelf, the compact hero, the theme toggle, the example list (including saving it), the shareable `?q=` link, the logo link, focus returning to the search box, the edition title headline, the cover fallback, the single featured link with its new-tab cue, the toast icon marks and a check that the `theme-color` metas match the page backgrounds.
- Health tests cover each probe (ok, failure statuses, timeout, unreachable host, 429 cooldown), configuration validation, the overall status rules, the 30-second cache, the 200 and 503 responses and the masked configuration dump.
- No test calls a live API.

## Docs

- Design: [docs/superpowers/specs/2026-10-08-find-that-book-design.md](docs/superpowers/specs/2026-10-08-find-that-book-design.md)
- Implementation plan (as built): [docs/superpowers/plans/2026-10-08-find-that-book.md](docs/superpowers/plans/2026-10-08-find-that-book.md)
- Decision record (ideas set aside, changes from the first draft): [docs/decisions.md](docs/decisions.md)
- Architecture diagrams (one file per diagram): [docs/architecture/](docs/architecture/)
- Product context: [PRODUCT.md](PRODUCT.md). Visual system: [DESIGN.md](DESIGN.md).

## With more time

Fail faster when Open Library does not answer, let a new search replace a running one, sort title-only searches by edition count, show an English display name for authors stored in another script, LLM re-ranking behind a flag, a persistent cache, ISBN matching, query logging for tuning, and a hosted live demo.
