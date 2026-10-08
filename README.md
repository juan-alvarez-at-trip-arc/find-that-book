# Find That Book

Turns a messy text query ("tolkien hobbit illustrated deluxe 1937") into up to 5 ranked Open Library matches, each with a one-sentence explanation. .NET 8 Web API, React UI, Gemini for query extraction and explanations.

## Run it

Requirements: .NET 8 SDK, Node 22+.

1. Get a free Gemini key at https://ai.google.dev/gemini-api/docs/api-key.
2. Store it (the app also runs without one, in fallback mode):
   ```bash
   cd src/FindThatBook.Api
   dotnet user-secrets init
   dotnet user-secrets set "Gemini:ApiKey" "<your key>"
   ```
3. API: `dotnet run --project src/FindThatBook.Api` (http://localhost:5080)
4. UI: `cd web && npm install && npm run dev` (http://localhost:5173, proxies `/api`)
5. Tests: `dotnet test` and `cd web && npm test`

Docker (UI and API on one port):

    docker build -t find-that-book .
    docker run --rm -p 8080:8080 -e Gemini__ApiKey=<your key> find-that-book

`Gemini__Model` overrides the model (default `gemini-2.5-flash`). AI provider: Gemini, as the brief suggests.

## API

`POST /api/search` with `{ "query": "..." }` returns `{ aiUsed, message, results: [{ title, author, authors, first_publish_year, workId, url, coverUrl, explanation }] }`.
Errors are RFC 7807 problem details: 400 for a blank query, 502 if Open Library is down. `GET /api/health` returns 200.

## How it works

1. `QueryParser`: Gemini turns the blob into `{ title?, author?, keywords[] }`. Without Gemini, a heuristic parser produces several hypotheses (whole text as title, as author, first or last word as author).
2. `OpenLibraryCatalog`: searches each hypothesis, then resolves primary authors from `/works/{id}.json` and `/authors/{id}.json`. Authors listed in search results but not on the work record are contributors.
3. `Normalizer` and `Matcher` (pure code): lowercase, strip diacritics, punctuation, subtitles and leading articles, merge initials, dedupe works, then rank by tier:
   1. exact title + primary author
   2. exact title + contributor-only author
   3. near title + author
   4. author-only (top works by editions)
   5. title match without a matching author

   Ties break on a matching year keyword, then edition count.
4. `ExplanationWriter`: Gemini writes one sentence per result from fetched facts only. Templates are the fallback.

## Decisions and assumptions

- Ranking is deterministic so the order is testable and explanations cannot contradict it. LLM re-ranking is left out.
- Every AI step degrades to a non-AI version and `aiUsed` becomes `false` (missing key, rate limit, timeout, malformed JSON).
- Tier 5 was added for title-only queries, which the brief's four tiers do not cover.
- Author-only queries use `/search.json?sort=editions` instead of `/authors/{id}/works.json`, which has no popularity order.
- Open Library calls are cached in memory for an hour, retried twice on 5xx/429/timeouts, and one failed enrichment call lowers a candidate's rank instead of failing the request.
- User text goes to Gemini as delimited data, and only the structured fields it returns are used.

## Testing

- Unit tests on `Normalizer` and `Matcher` carry most coverage (every tier, contributors, subtitles, diacritics, initials, dedupe, ties).
- Parser, writer and client tests use a fake Gemini client and a stub `HttpMessageHandler`, including malformed JSON and HTTP errors.
- Integration tests use `WebApplicationFactory` with a fake catalog, covering fallback mode, AI mode, 400, 502 and empty queries.
- Frontend tests (Vitest + Testing Library) cover results, the no-AI notice, empty and error states.
- No test calls a live API.

## With more time

LLM re-ranking behind a flag, a persistent cache, edition and ISBN matching, language filtering, query logging for tuning, rate limiting.
