# Find That Book: Design

Source brief: `docs/2025_Developer_Project.md`.

## Goal

Submission for the InfoTrack "Find That Book" challenge. A messy text query goes in. Up to 5 ranked Open Library matches come out, each with a one-sentence explanation grounded in fetched fields. Scope stays small enough to build in 4-6 hours and present.

## Decisions

| # | Decision |
|---|----------|
| D1 | Frontend: React + Vite + TypeScript, built and served by the API in one container |
| D2 | AI extracts the query and writes explanations. Ranking is deterministic code. No LLM re-ranking |
| D3 | If Gemini fails, degrade to a heuristic parser and template explanations. Response carries `aiUsed: false` |
| D4 | Layered .NET 8 solution: `Api`, `Application`, `Infrastructure`, `Tests` |
| D5 | Live demo: single container on Azure Container Apps or Render. Stretch step, does not block core work |
| D6 | Open Library strategy: search, then enrich. Author-only queries use `/authors/{id}/works.json` |
| D7 | AI model: Gemini Flash (model name configurable), per the brief's default |

## API

`POST /api/search`

Request: `{ "query": "tolkien hobbit illustrated deluxe 1937" }`

Response 200:

```json
{
  "aiUsed": true,
  "message": null,
  "results": [
    {
      "title": "The Hobbit",
      "author": "J.R.R. Tolkien",
      "authors": ["J.R.R. Tolkien"],
      "first_publish_year": 1937,
      "workId": "OL262758W",
      "url": "https://openlibrary.org/works/OL262758W",
      "coverUrl": "https://covers.openlibrary.org/b/id/....-M.jpg",
      "explanation": "Exact title match; Tolkien is primary author; Dixon listed as adaptor."
    }
  ]
}
```

- `author` is the joined primary authors (matches the brief's example shape). `authors` is the list.
- `coverUrl` is null when unavailable.
- No results: 200 with `results: []` and a `message`.
- Errors use RFC 7807 problem details: 400 invalid input, 502 Open Library unavailable.

`GET /api/health` returns 200 for deploy checks.

## Pipeline

1. **QueryParser** (Gemini, heuristic fallback) returns `ParsedQuery { title?, author?, keywords[] }`. Gemini uses a JSON response schema. The result is validated before use.
2. **OpenLibraryClient** calls `/search.json` with `title` and `author` params, falling back to `q` when both are empty. It then calls `/works/{id}.json` for the top ~8 candidates to resolve primary authors from `works.authors`, and `/authors/{id}.json` for names. Author-only queries resolve the author and call `/authors/{id}/works.json`.
3. **Normalizer** lowercases, strips diacritics and punctuation, strips subtitles ("The Hobbit" vs "The Hobbit, or There and Back Again"), and dedupes results to canonical work keys.
4. **Matcher** assigns a tier and sorts by tier, then tie-breakers (edition count, publish year matching a year keyword). Keeps the top 5.
   - Tier 1: exact normalized title + primary author match
   - Tier 2: exact normalized title + contributor-only author
   - Tier 3: near-match title + author match
   - Tier 4: author-only fallback, top works by that author
5. **ExplanationWriter** (Gemini, template fallback) writes one sentence per candidate from fetched fields only: tier, primary authors, contributors. The prompt forbids facts outside those fields.

Primary authors come from canonical work records. `author_name` from search results is contributor-level signal only.

## Components

| Component | Layer | Responsibility | Depends on |
|-----------|-------|----------------|------------|
| `SearchController` | Api | HTTP, validation, problem details | `ISearchService` |
| `SearchService` | Application | Orchestrates the pipeline | parser, catalog client, matcher, writer |
| `IQueryParser` | Application | Interface for step 1 | none |
| `IBookCatalog` | Application | Interface for step 2 | none |
| `IExplanationWriter` | Application | Interface for step 5 | none |
| `Normalizer`, `Matcher` | Application | Pure logic, steps 3 and 4 | none |
| `GeminiQueryParser`, `GeminiExplanationWriter` | Infrastructure | Gemini calls | `GeminiClient` |
| `HeuristicQueryParser`, `TemplateExplanationWriter` | Application | AI-free fallbacks | none |
| `OpenLibraryCatalog` | Infrastructure | HTTP, retry, cache | `HttpClient` |

AI-backed implementations are wrapped by a fallback decorator that catches failure and delegates to the heuristic or template version, then sets `aiUsed = false`.

## Error handling

- Empty or whitespace query: 400. Queries over 200 characters are truncated before parsing.
- Open Library: 10s timeout, 2 retries with backoff on 5xx and timeouts, small in-memory cache. Search failure returns 502. A failed `/works` or `/authors` call keeps the candidate, drops it to lower rank, and says so in the explanation.
- Gemini failures (missing key, 429, timeout, invalid JSON) trigger the fallback path with `aiUsed: false`.
- Global exception handler returns problem details with no stack traces.

## Testing

- Unit: `Normalizer` and `Matcher` (each tier, contributor-only authors, subtitles, diacritics, ties). This is the bulk of coverage.
- Unit: `GeminiQueryParser` and `GeminiExplanationWriter` with a fake Gemini client (success, invalid JSON, failure triggers fallback). `HeuristicQueryParser` and `TemplateExplanationWriter` directly.
- Integration: `WebApplicationFactory` with fake `IBookCatalog` and fake Gemini, using fixtures for `tolkien hobbit illustrated deluxe 1937`, `mark huckleberry`, `austen bennet`, `dickens`, `tale two cities`, `twilight meyer`.
- No test calls a live API. An optional smoke script hits the real services.
- Frontend: Vitest + React Testing Library for loading, error, empty and `aiUsed: false` states.

## Frontend

Single page: search box, example-query chips, result cards (cover, title, author, year, explanation, Open Library link), loading skeletons, empty state, error banner, and a notice when `aiUsed` is false. Built with Vite, output copied into the API's `wwwroot` by the Dockerfile.

## Config and run

- `Gemini__ApiKey` via user-secrets or an environment variable. `Gemini__Model` optional, defaults to a Gemini Flash model.
- Without a key the app runs in fallback mode.
- One `Dockerfile` builds the frontend and the API into a single image.
- README covers setup, implementation overview, assumptions and decisions, features, testing strategy, and future work.

## Out of scope

LLM re-ranking, persistent cache, edition-level matching, authentication, rate limiting beyond Open Library retry behavior.

## Future improvements (README)

LLM re-ranking behind a flag, persistent or distributed cache, edition-level and ISBN matching, query logging for tuning, language filtering.
