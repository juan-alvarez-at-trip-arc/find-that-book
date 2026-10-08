# Find That Book

Turns a messy text query ("tolkien hobbit illustrated deluxe 1937") into up to 5 ranked Open Library matches, each with a one-sentence explanation. .NET 8 Web API, React UI, and an AI provider for query extraction and explanations: Gemini (the brief's default) or any OpenAI-compatible endpoint (Groq, Cerebras, Ollama, OpenRouter, Mistral). See [AI providers](#ai-providers).

## Features

- Accepts sparse, noisy or ambiguous queries: a title only, an author only, both with extra words, partial names ("mark huckleberry"), misspelled or non-English titles.
- Ranks by a deterministic matching hierarchy (primary authors above contributors), so the order is testable and explanations cannot contradict it.
- AI extraction and explanations with automatic fallback to a heuristic parser and template explanations. The app works with no AI key.
- Matches work titles, edition titles (a work titled in another language still matches its English edition), author aliases and transliteration variants ("Dostoevsky" and "Dostoievski").
- Gentle with Open Library: capped lookups, a request limit and a cooldown after HTTP 429.
- UI: result cards with covers, a timed progress panel, light/dark/system theme and example chips that promote your successful searches. See [web/README.md](web/README.md).

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
  FindThatBook.Api/             controller, problem details, static files, appsettings
  FindThatBook.Application/     models, interfaces, Normalizer, Matcher, heuristic parser,
                                template explanations, fallback decorators, SearchService
  FindThatBook.Infrastructure/  Open Library client (+ request gate), AI seam (IAiClient),
                                Gemini and OpenAI-compatible clients, DI registration
tests/FindThatBook.Tests/       xUnit tests (stub HTTP handlers, fake AI client and catalog)
web/                            React + Vite + TypeScript UI (see web/README.md)
docs/superpowers/               design spec and implementation plan
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

## API

`POST /api/search` with `{ "query": "..." }` returns `{ aiUsed, message, results: [{ title, author, authors, first_publish_year, workId, url, coverUrl, explanation }] }`. The query is trimmed and truncated to 200 characters.
Errors are RFC 7807 problem details: 400 for a blank query, 502 if Open Library cannot be searched, 500 for anything else. `GET /api/health` returns 200.

## How it works

1. **Parse.** The AI provider turns the text into `{ title?, author?, keywords[] }`, one hypothesis. Without AI, a heuristic parser strips noise words and years into keywords and emits up to six guessed hypotheses (whole text as title, as author, first or last word as author, first or last two words as author).
2. **Search.** `OpenLibraryCatalog` searches every hypothesis in parallel, asking Open Library for matching edition titles too. Works are then looked up (at most 6 per search) to find their primary authors, with each author's alternate names kept for matching. Authors listed in search results but not on the work record are contributors.
3. **Match and rank** (pure code, `Normalizer` and `Matcher`). Text is lowercased, with diacritics, apostrophes, punctuation, subtitles and leading articles removed and initials merged. A title matches exactly or nearly against the work title or an edition title. An author matches when every query word equals or starts a name word, through aliases and transliteration variants. Tiers:
   1. exact title + primary author
   2. exact title + contributor-only author
   3. near title + author
   4. author only (top works by editions)
   5. title match without a matching author

   Results sort by strength: tiers 1 and 2, tier 3 from an AI-extracted author, tier 4, an exact title alone, tier 3 from a guessed author, then a near title alone. Ties break on a matching year keyword, then edition count. Works are deduplicated by full title and first primary author, and the top 5 are kept.
4. **Explain.** The AI provider writes one sentence per result from fetched facts only (the publication year is given only when it matched the query, so it cannot be misstated). Template explanations are the fallback.

## Decisions and assumptions

- Ranking is deterministic so the order is testable and explanations cannot contradict it. LLM re-ranking is left out.
- Every AI step degrades to a non-AI version and `aiUsed` becomes `false` (missing key, rate limit, timeout, malformed JSON).
- Tier 5 was added for title-only queries, which the brief's four tiers do not cover.
- A guessed author split ranks below an exact title on its own, so "the little prince" finds the book before a "Little Pigs" book by an author named Prince.
- Author-only queries use `/search.json?sort=editions` instead of `/authors/{id}/works.json`, which has no popularity order.
- Open Library data quirks are handled in matching: a work can be titled in another language ("海辺のカフカ") while its English edition carries the title the reader typed, and authors can be stored in another script. Titles and names are shown as Open Library stores them.
- Open Library calls are cached in memory for an hour and retried twice on 5xx and timeouts. A failed detail lookup lowers a candidate's rank instead of failing the request.
- The app is gentle with Open Library, which rate-limits (HTTP 429) heavy use. Each search looks up details for at most 6 works and 3 authors per work (`OpenLibrary:MaxDetailLookups`, `MaxAuthorsPerWork`), and at most 2 requests run at the same time (`MaxConcurrentRequests`). A 429 is never retried: it pauses all Open Library requests for the `Retry-After` time, or 30 s (`DefaultCooldownSeconds`), and during that pause searches fail fast with a 502 while cached lookups still work.
- Open Library asks API users to identify themselves. Set `OpenLibrary__Contact` (an email or URL, or `OPENLIBRARY_CONTACT` for Docker Compose) and it is added to the `User-Agent`. It is empty by default and never filled in automatically.
- User text goes to the AI provider as delimited data, and only the structured fields it returns are used.

## Known limitations

- Pressing Enter while a search is running does nothing, because the Search button is disabled during a search.
- When the AI provider hangs, a search can wait up to about 40 seconds before falling back (two AI steps at 30 seconds each).
- In fallback mode the guessed hypotheses can still produce odd results, and summaries or study guides can appear among results.
- Different first-name spellings (for example Fyodor and Fedor) only match when the surname also matches.
- The example chips and their order are not saved across page reloads.
- The Docker image has not been built in this environment (the Docker daemon was not available), although the same layout was run by hand and `docker compose config` accepts the compose file.

## Testing

- 196 .NET tests. `Normalizer` and `Matcher` carry most coverage (every tier, contributors, subtitles, diacritics, apostrophes, initials, transliteration, dedupe, ties, guessed versus extracted authors, edition titles, aliases).
- Parser, writer and client tests use a fake AI client and stub `HttpMessageHandler`s, including malformed JSON, HTTP errors, schema conversion and provider selection.
- Open Library catalog tests cover parsing, retries, enrichment failures, lookup caps, the concurrency limit and the 429 cooldown.
- Integration tests use `WebApplicationFactory` with a fake catalog, covering fallback mode, AI mode, 400, 502 and empty queries.
- 31 frontend tests (Vitest + Testing Library) cover results, notices, empty and error states, stale responses, the progress panel, the theme toggle and the example list.
- No test calls a live API.

## Docs

- Design: [docs/superpowers/specs/2026-10-08-find-that-book-design.md](docs/superpowers/specs/2026-10-08-find-that-book-design.md)
- Implementation plan (as built): [docs/superpowers/plans/2026-10-08-find-that-book.md](docs/superpowers/plans/2026-10-08-find-that-book.md)

## With more time

Fail faster when Open Library does not answer and add a Retry button on the error banner, let a new search replace a running one, sort title-only searches by edition count, persist the example chips, show an English display name for authors stored in another script, LLM re-ranking behind a flag, a persistent cache, ISBN matching, query logging for tuning, and a hosted live demo.
