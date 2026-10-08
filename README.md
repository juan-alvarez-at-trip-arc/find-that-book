# Find That Book

Turns a messy text query ("tolkien hobbit illustrated deluxe 1937") into up to 5 ranked Open Library matches, each with a one-sentence explanation. .NET 8 Web API, React UI, and an AI provider for query extraction and explanations: Gemini (the brief's default) or any OpenAI-compatible endpoint (Groq, Cerebras, Ollama, OpenRouter, Mistral). See [AI providers](#ai-providers).

## Run it

Requirements: .NET 8 SDK, Node 22+.

1. Get an API key. Gemini: https://ai.google.dev/gemini-api/docs/api-key. For an OpenAI-compatible provider such as Groq, see [AI providers](#ai-providers). The app also runs without any key, in fallback mode.
2. Store it. For the default Gemini provider:
   ```bash
   cd src/FindThatBook.Api
   dotnet user-secrets init
   dotnet user-secrets set "Gemini:ApiKey" "<your key>"
   ```
3. API: `dotnet run --project src/FindThatBook.Api` (http://localhost:5080)
4. UI: `cd web && npm install && npm run dev` (http://localhost:5173, proxies `/api`)
5. Tests: `dotnet test` and `cd web && npm test`

Docker Compose (UI and API on one port, http://localhost:8080):

1. Optional: put your settings in a `.env` file next to `docker-compose.yml` (without a key the app runs in fallback mode):
   ```
   # Gemini (default provider)
   GEMINI_API_KEY=<your key>
   GEMINI_MODEL=gemini-3.8-flash

   # Or an OpenAI-compatible provider (here: Groq)
   AI_PROVIDER=OpenAiCompatible
   OPENAI_API_KEY=<your key>
   OPENAI_BASE_URL=https://api.groq.com/openai/v1/
   OPENAI_MODEL=openai/gpt-oss-20b
   ```
   Every variable is optional. Blank values use the defaults.
2. Start it: `docker compose up --build`
3. Stop it: `docker compose down`

Plain Docker works too:

    docker build -t find-that-book .
    docker run --rm -p 8080:8080 -e Gemini__ApiKey=<your key> find-that-book

## AI providers

AI is used for two steps: extracting `{ title?, author?, keywords[] }` from the query, and writing one explanation per result. Pick the provider with `Ai:Provider` (env var `Ai__Provider`):

| `Ai:Provider` | Client | Settings (env var form) |
|---|---|---|
| `Gemini` (default) | Gemini `generateContent` | `Gemini__ApiKey`, `Gemini__Model` (default `gemini-3.8-flash`) |
| `OpenAiCompatible` | any `/chat/completions` endpoint | `OpenAiCompatible__ApiKey`, `OpenAiCompatible__BaseUrl` (default `https://api.groq.com/openai/v1/`), `OpenAiCompatible__Model` (default `openai/gpt-oss-20b`) |

A blank model or base URL uses the default, and an unknown provider name stops the app at startup with a clear error. The OpenAI-compatible key is optional because local servers need none.

Examples for `OpenAiCompatible`:

| Provider | `BaseUrl` | Example `Model` | Key |
|---|---|---|---|
| Groq | `https://api.groq.com/openai/v1/` | `openai/gpt-oss-20b` | https://console.groq.com/keys |
| Cerebras | `https://api.cerebras.ai/v1/` | see their model list | https://cloud.cerebras.ai |
| Ollama (local) | `http://localhost:11434/v1/` | any pulled model | none |

Model names and free-tier limits change often, so check the provider's own pages. Whichever provider you pick, every AI step still degrades to the non-AI parser and templates on any failure, and the response reports `aiUsed: false`.

How the OpenAI-compatible client works: it posts a `chat/completions` request with `response_format: {"type": "json_object"}` (the mode most providers support), puts the expected JSON Schema in the system message, and wraps array-shaped results in a `{"result": ...}` object because JSON mode needs an object at the root. The reply is parsed and validated by the same code as for Gemini.

### Why Gemini alone is not reliable enough

We built the OpenAI-compatible option because Gemini's free tier was unreliable when we tested it. Observations from 2026-10-08 with one free-tier key (these describe that key and day, not Gemini in general):

- The original default `gemini-2.5-flash` returned 404: "no longer available to new users". Google's pricing page still listed it, so a default that looks valid can stop working without notice.
- `gemini-3.8-flash`, `gemini-3.7-flash`, `gemini-3.6-flash`, `gemini-3.5-flash-lite`, `gemini-3.1-flash-lite` and `gemini-flash-latest` returned 503 "high demand" on repeated attempts over a long session. The app also received 429 rate limits.
- `gemini-3.5-flash` answered but took about 22 seconds for a single extraction.
- Result: most requests fell back to the heuristic parser, which works but is less precise (for example "austen bennet" does not find Pride and Prejudice).

The fallback keeps the app usable, but it hides the problem from users, so having a second, selectable provider is the practical fix. Switching is configuration only, with no code change.

## API

`POST /api/search` with `{ "query": "..." }` returns `{ aiUsed, message, results: [{ title, author, authors, first_publish_year, workId, url, coverUrl, explanation }] }`.
Errors are RFC 7807 problem details: 400 for a blank query, 502 if Open Library is down. `GET /api/health` returns 200.

## How it works

1. `QueryParser`: the AI provider turns the blob into `{ title?, author?, keywords[] }`. Without AI, a heuristic parser produces several hypotheses (whole text as title, as author, first or last word as author).
2. `OpenLibraryCatalog`: searches each hypothesis, then resolves primary authors from `/works/{id}.json` and `/authors/{id}.json`. Authors listed in search results but not on the work record are contributors.
3. `Normalizer` and `Matcher` (pure code): lowercase, strip diacritics, punctuation, subtitles and leading articles, merge initials, dedupe works, then rank by tier:
   1. exact title + primary author
   2. exact title + contributor-only author
   3. near title + author
   4. author-only (top works by editions)
   5. title match without a matching author

   Ties break on a matching year keyword, then edition count.
4. `ExplanationWriter`: the AI provider writes one sentence per result from fetched facts only. Templates are the fallback.

## Decisions and assumptions

- Ranking is deterministic so the order is testable and explanations cannot contradict it. LLM re-ranking is left out.
- Every AI step degrades to a non-AI version and `aiUsed` becomes `false` (missing key, rate limit, timeout, malformed JSON).
- Tier 5 was added for title-only queries, which the brief's four tiers do not cover.
- Author-only queries use `/search.json?sort=editions` instead of `/authors/{id}/works.json`, which has no popularity order.
- Open Library calls are cached in memory for an hour, retried twice on 5xx/429/timeouts, and one failed enrichment call lowers a candidate's rank instead of failing the request.
- User text goes to the AI provider as delimited data, and only the structured fields it returns are used.

## Testing

- Unit tests on `Normalizer` and `Matcher` carry most coverage (every tier, contributors, subtitles, diacritics, initials, dedupe, ties).
- Parser, writer and client tests use a fake AI client and a stub `HttpMessageHandler`, including malformed JSON and HTTP errors.
- Integration tests use `WebApplicationFactory` with a fake catalog, covering fallback mode, AI mode, 400, 502 and empty queries.
- Frontend tests (Vitest + Testing Library) cover results, the no-AI notice, empty and error states.
- No test calls a live API.

## With more time

LLM re-ranking behind a flag, a persistent cache, edition and ISBN matching, language filtering, query logging for tuning, rate limiting.
