# Find That Book: Design

Source brief: `docs/2025_Developer_Project.md`.

Status: this document describes the application as built on `master`. The first draft is summarized under "Changes from the first draft". Ideas that were tried or set aside are in `docs/decisions.md`.

## Goal

Submission for the InfoTrack "Find That Book" challenge. A messy text query goes in. Up to 5 ranked Open Library matches come out, each with a one-sentence explanation grounded in fetched fields. Scope stays small enough to build in 4-6 hours and present, with the extras listed below added afterwards.

## Decisions

| # | Decision |
|---|----------|
| D1 | Frontend: React + Vite + TypeScript, built and served by the API in one container. Source is split into `components/<Name>/` and `lib/` |
| D2 | AI extracts the query and writes explanations. Ranking is deterministic code. No LLM re-ranking |
| D3 | Every AI step has a non-AI fallback (heuristic parser, template explanations). The response carries `aiUsed: false` when any step fell back |
| D4 | Layered .NET 8 solution: `Api`, `Application`, `Infrastructure`, `Tests` |
| D5 | One `Dockerfile` (frontend + API in one image) and a `docker-compose.yml`. A hosted live demo was not deployed |
| D6 | Open Library strategy: search, then enrich. Author-only queries use `/search.json?sort=editions`, not `/authors/{id}/works.json`, which has no popularity order |
| D7 | AI provider is selectable by configuration: `Gemini` (default, per the brief) or `OpenAiCompatible` (Groq, Cerebras, Ollama, OpenRouter, Mistral). Gemini defaults to `gemini-3.8-flash` |
| D8 | The parser returns a list of hypotheses. The AI returns one, the heuristic parser up to six. The catalog searches each and the matcher scores every candidate against all of them |
| D9 | Tier 5 (`TitleOnly`) covers a title match with no matching author. Hypotheses guessed by the heuristic parser rank below an exact title on its own |
| D10 | Titles are matched against the work title and the matching edition titles. Authors are matched against names, aliases and transliteration variants |
| D11 | Open Library is treated gently: capped detail lookups, a shared request limit, a cooldown after HTTP 429, and an optional contact in the `User-Agent` |
| D12 | UI extras: timed progress panel, light/dark/system theme, example chips that promote successful searches |
| D13 | UI design: "Bookshelf" (cover-first results with match badges), chosen from three proposals kept in `docs/ui-proposals/`, then changed to a featured layout (top match large with its reason, other matches as compact cards, see UI spec D20). The layout works on phones |
| D14 | The API returns `matchTier` and `exactTitle` per result so the UI can badge matches without parsing the explanation |

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
      "workId": "OL27482W",
      "url": "https://openlibrary.org/works/OL27482W",
      "coverUrl": "https://covers.openlibrary.org/b/id/14627509-M.jpg",
      "explanation": "Exact title match; J.R.R. Tolkien is primary author; first published 1937, matching the query.",
      "matchTier": 1,
      "exactTitle": true
    }
  ]
}
```

- `matchTier` is 1 (strongest) to 5, see the tiers under "Pipeline". `exactTitle` is true when the title matched exactly rather than nearly.
- `author` is the joined primary authors. `authors` is the list. When a work's primary authors could not be resolved, the first contributor is used.
- `first_publish_year` is the only snake_case field, matching the brief's example.
- `coverUrl` is null when unavailable.
- The query is trimmed and truncated to 200 characters.
- No results: 200 with `results: []` and a `message`. A weak best match returns the results with a `message` saying there is no confident match.
- Errors use RFC 7807 problem details: 400 for a blank query ("Query is required."), 502 when Open Library cannot be searched ("Open Library is unavailable. Please try again shortly."), 500 for anything else.

Health endpoints, all in `HealthController`:

- `GET /api/health` returns `{ "status": "ok" }` for deploy checks and makes no outside calls.
- `GET /api/health/ready` validates configuration and probes Open Library and the selected AI provider, cached for 30 seconds. Open Library is critical: if it is down or the configuration is invalid the status is `unready` (503). A missing, rejected or failing AI provider gives `degraded` (200), because searches fall back to the heuristic parser. Probes cost no AI tokens (Gemini model lookup, OpenAI-compatible `GET /models`), time out after 5 seconds and skip the shared Open Library request gate, except that an active 429 cooldown counts as down.
- `GET /api/health/config` returns the effective configuration. API keys are masked (the first and last four characters stay visible, keys shorter than 12 characters are fully masked, a missing key shows `not set`) and URL credentials are removed. It answers only callers on the local machine (a missing, loopback or local-equal remote address) and returns 404 to all others.

## Pipeline

1. **QueryParser** returns `ParseOutcome { Hypotheses, AiUsed }`. Each hypothesis is `ParsedQuery { Title?, Author?, Keywords[], Guessed }`.
   - AI parser: one JSON-mode call with a provider-neutral JSON Schema. The query text is delimited as data. A reply with neither title nor author, or malformed JSON, counts as AI failure.
   - Heuristic parser: removes noise words (`illustrated`, `deluxe`, `edition`, `hardcover`, `paperback`, `novel`, `book`, `by`, `unabridged`, `audiobook`, `signed`, `reprint`, `anniversary`, `collectors`) and 1500-2099 years into keywords. It then emits up to six hypotheses, all flagged `Guessed`: whole text as title, whole text as author, first word or last word as author, and (three or more words) first two or last two words as author. Text with no words left yields no hypotheses.
2. **OpenLibraryCatalog** searches every hypothesis in parallel.
   - Request: `/search.json` with `title` and/or `author`, plus `sort=editions` when there is no title, `limit=5`, and `fields` including `editions` (the matching edition titles). When a fielded search finds fewer than three works, a free-text (`q`) search also runs and its new works are appended (the fielded search can miss a work whose title is in another language, for example "Män som hatar kvinnor" for "the girl with the dragon tattoo"). If only the free-text search fails, the fielded results are kept.
   - Detail lookups are capped (6 works per search, taken round-robin across hypotheses). Each work gets `/works/{id}.json` for its primary authors, then `/authors/{id}.json` for up to 3 authors: the display `name`, plus `personal_name` and `alternate_names` as aliases used only for matching.
   - Authors named in search results but not on the work record are contributors.
   - A failed work or author lookup keeps the candidate as `Enriched = false`, ranks it by contributor-level signal, and the explanation says author details are unavailable.
3. **Normalizer** (pure): lowercases, strips diacritics, apostrophes and punctuation, merges initials ("J.R.R." becomes `jrr`), cuts subtitles and parentheticals, removes leading articles, and builds name tokens that fold transliteration variants (`y`/`j` to `i`, `tch` to `ch`, `ie` to `e`, trailing `ii` to `i`).
4. **Matcher** (pure) assigns each candidate its best match across all hypotheses, then sorts.
   - Title match: compared with the work title, then the edition titles. Exact is equal normalized keys. Near is one title's words contained in the other's, or word overlap (Jaccard) of at least 0.6.
   - Author match: every query token equals, or (3+ characters) prefixes, a name token. Primary authors are checked first, then their aliases, then contributors.
   - Tiers: 1 exact title + primary author, 2 exact title + contributor-only author, 3 near title + author, 4 author only (no title in the hypothesis), 5 title match with no matching author.
   - Sort order (lower is stronger): tier 1, tier 2, tier 3 from an AI hypothesis, tier 4, tier 5 with an exact title, tier 3 from a guessed hypothesis, tier 5 with a near title. Ties break on exact title, then a year keyword matching the first publish year, then edition count, then title.
   - Candidates are deduplicated by full title (subtitle kept) and first primary author, keeping the one with the most editions. Candidates whose title contains a study-aid phrase (`summary`, `study guide`, `sparknotes`, `cliffsnotes`, `cliffs notes`) are dropped unless the query contains that phrase. The top 5 are kept.
   - `Matcher.IsConfident` is false when the best match has strength 60 or more (tier 3 from a guessed hypothesis, or tier 5 with a near title). `SearchService` then adds the "No confident match" message to the results.
5. **ExplanationWriter** writes one sentence per result from fetched facts only.
   - AI writer facts: index, title, primary authors, contributors, match kind, exact-title flag, matched author and its role, whether author details are available. `firstPublishYear` and `yearMatchesQuery` are sent only when the year matched the query, so the model cannot misstate it. `matchedEditionTitle` is sent when the match came through an edition title.
   - Template writer: deterministic wording from the same facts.

`aiUsed` is true only when both the parser and the writer used the AI.

## Components

Backend (`src/`):

| Component | Layer | Responsibility |
|-----------|-------|----------------|
| `SearchController`, `HealthController`, `ApiExceptionHandler`, `Program` | Api | HTTP, validation, problem details, static files, health endpoints |
| `SearchService` | Application | Orchestrates the pipeline, truncates the query, builds the response |
| `IQueryParser`, `IBookCatalog`, `IExplanationWriter`, `ISearchService` | Application | Interfaces; `AiUnavailableException` and `CatalogUnavailableException` |
| `Normalizer`, `Matcher` | Application | Pure logic, steps 3 and 4 |
| `HeuristicQueryParser`, `TemplateExplanationWriter` | Application | AI-free fallbacks |
| `FallbackQueryParser`, `FallbackExplanationWriter` | Application | Decorators that catch only `AiUnavailableException` and delegate to the fallback |
| `IAiClient`, `AiSchemas`, `AiQueryParser`, `AiExplanationWriter` | Infrastructure | Provider-neutral AI seam, schemas and prompts |
| `GeminiClient`, `GeminiSchema`, `GeminiOptions` | Infrastructure | Gemini `generateContent`; converts the neutral schema to Gemini's dialect |
| `OpenAiCompatibleClient`, `OpenAiCompatibleOptions` | Infrastructure | Any `/chat/completions` endpoint in JSON mode |
| `OpenLibraryCatalog`, `OpenLibraryGate`, `OpenLibraryOptions` | Infrastructure | Open Library HTTP, retry, cache, request limit and 429 cooldown |
| `HealthReportService`, `HealthConfigCheck`, `ConfigDump`, `OpenLibraryProbe`, `GeminiProbe`, `OpenAiCompatibleProbe`, `ProbeRunner` | Infrastructure | Readiness report (cached 30 s), configuration validation, masked configuration dump, provider probes |
| `ServiceCollectionExtensions` | Infrastructure | Registration; selects the AI client from `Ai:Provider` |

Frontend (`web/src/`):

| Path | Responsibility |
|------|----------------|
| `components/App/` | Page (hero search, example chips, the toast container, results count) and `App.css` with the theme tokens, featured results and mobile styles |
| `components/FeaturedResults/` | The top match large with its explanation, and the other matches as compact cards with a badge and a collapsible "Why this book?" |
| `components/ResultsSkeleton/` | Placeholder shaped like the featured layout while a search runs |
| `components/SearchProgress/` | Animated, timed progress panel |
| `components/ThemeToggle/` | System, Light and Dark toggle |
| `components/Toast/` | Dismissable message (error, notice, info) shown in a fixed stack |
| `lib/` | `api.ts` (fetch), `types.ts`, `theme.ts` (storage and attribute), `examples.ts` (promotion rule), `badge.ts` (badge from tier, exact title and rank) |

## AI providers

- `Ai:Provider` is `Gemini` (default), `OpenAiCompatible`, or blank (Gemini). An unknown value stops the app at startup with a clear message.
- Gemini: `POST v1beta/models/{model}:generateContent` with the key in `x-goog-api-key`, `responseMimeType: application/json`, `responseSchema` converted from the neutral schema, temperature 0. A blank model uses the default.
- OpenAI-compatible: `POST {BaseUrl}/chat/completions` with `response_format: json_object`, `stream: false`, temperature 0, the JSON Schema in the system message, and a Bearer key when one is set (optional, for local servers). Array-shaped schemas are wrapped as `{"result": ...}` and unwrapped from the reply. Defaults: Groq endpoint and `openai/gpt-oss-20b`.
- Both clients turn every failure (missing key, HTTP error, timeout, empty or malformed reply) into `AiUnavailableException`, which the decorators handle. Both HTTP clients time out at 30 seconds.
- The README records why Gemini alone was not reliable enough when tested (a default model that stopped being available to new users, repeated 503 and 429 responses).

## Error handling

- Blank query: 400. Over 200 characters: truncated.
- Open Library: 10 s per attempt, 2 retries with growing backoff on 5xx, timeouts and network errors, in-memory cache for an hour (size-limited to 2000 entries). If no hypothesis can be searched, 502.
- HTTP 429 is never retried. It starts a shared cooldown (`Retry-After`, or 30 s) during which every Open Library request fails fast without being sent. Cached lookups still work.
- At most 2 Open Library requests run at once across the process.
- Optional `OpenLibrary:Contact` (an email or URL) is added to the `User-Agent`. It is empty by default.
- AI failures of any kind fall back with `aiUsed: false`.
- The global exception handler returns problem details with no stack traces.

## Frontend

Single page with: a hero search box (compact after the first search), example chips, featured results with a focused results heading, a theme toggle, and a progress panel followed by a placeholder for the results. Messages are dismissable toasts: an error toast with a Try again button, a notice toast when `aiUsed` is false, an information toast for the weak-match message and an information toast for the empty state with a tip. Details are in `docs/ui-proposals/ui-spec.md` (D13 to D21).

- Featured results: the top match is large (cover, badge, title, author and year, its explanation, a link to Open Library). The other matches are compact cards in 2 columns (1 up to 760px wide) under "Other possible matches". A book without a cover gets a generated blue cover showing its title.
- Badges: `Best match` (first result with an exact title), `Exact title`, `Author match` (tier 4) and `Close title` (everything else), chosen by `lib/badge.ts`.
- Explanation: always visible for the top match. The other cards use a native `<details>` "Why this book?" disclosure, collapsed by default. Covers link to the Open Library page.
- Mobile: `viewport-fit=cover` with safe-area padding, tap targets of at least 36 to 44px, example chips in one horizontally scrolling row, and a header that wraps at 420px so the theme toggle does not overflow.
- Design: the three proposals (Reading Room, Bookshelf, Modern Catalog) are static mockups in `docs/ui-proposals/`. Bookshelf was implemented first and then changed to the featured layout.

- Starting a search clears the previous results, notice and error. A late response from an older search is ignored.
- The search box, the example chips and the Search button are disabled while a search runs, so pressing Enter during a search does nothing.
- Progress panel: gradient bar, bouncing dots and four pips, with a status line that advances every 2.5 seconds through four steps and then says the search is taking longer than usual. The steps are timed, not driven by the server. Animations stop under `prefers-reduced-motion`.
- Examples: five chips at start. A search with at least one result moves its text to the front (case-insensitive, an existing chip is moved, otherwise the last chip is dropped). Failed and empty searches do not change the list. The list is saved in `localStorage` under `ftb-examples`.
- Shareable searches: every search puts its text in the `q` parameter of the page URL (`history.replaceState`), and opening a page with `?q=...` fills the box (up to 200 characters) and runs that search once.
- Theme: System (default), Light or Dark through `data-theme` on `<html>` plus `color-scheme`, with colors defined as CSS variables for each mode. The choice is stored under `ftb-theme` with every storage access guarded, and a small inline script in `index.html` applies it before first paint.

## Config and run

- Settings come from `appsettings.json`, user secrets and environment variables (`Ai__Provider`, `Gemini__ApiKey`, `OpenAiCompatible__BaseUrl`, `OpenLibrary__Contact` and so on). The appsettings sections are `Ai`, `Gemini`, `OpenAiCompatible` and `OpenLibrary` (`BaseUrl`, `MaxDetailLookups`, `MaxAuthorsPerWork`, `MaxConcurrentRequests`, `DefaultCooldownSeconds`, `Contact`).
- `docker-compose.yml` maps `.env` variables (`AI_PROVIDER`, `GEMINI_API_KEY`, `GEMINI_MODEL`, `OPENAI_API_KEY`, `OPENAI_BASE_URL`, `OPENAI_MODEL`, `OPENLIBRARY_*`) to those settings. `.env` is git-ignored. `dotnet run` does not read it.
- Without any key the app runs in fallback mode.
- The API serves the built frontend from `wwwroot` (copied from `web/dist` by the Dockerfile).
- Production code under `src/` carries XML documentation comments.

## Testing

- .NET (287 tests): `Matcher` (tiers, ranking, study-aid filter, confidence, guessed hypotheses, edition titles, aliases, transliteration, dedupe), `Normalizer`, heuristic parser, template and AI explanation writers, Gemini and OpenAI-compatible clients (with stub HTTP handlers), schema conversion, provider selection, Open Library catalog (parsing, retries, enrichment, caps, concurrency limit, 429 cooldown), fallback decorators, `SearchService`, health (probes, configuration validation, status rules, cache, masked configuration dump), and `WebApplicationFactory` integration tests with a fake catalog.
- Frontend (58 tests, Vitest + Testing Library): the disabled search box and example chips during a search, results, badges, the featured results and their collapsed reasons, the focused results heading, Try again, the skeleton, the compact hero, notices (including the weak-match notice), the `?q=` link, saved examples, empty and error states, stale-response handling, progress panel, theme toggle, example promotion. Layout and mobile styles were checked by hand in Chrome (dark mode, a window about 250px wide), not by automated tests.
- No test calls a live API.

## Known limitations

- Pressing Enter while a search runs is ignored (the search box and the button are disabled).
- When the AI provider hangs, a search can wait up to about 40 seconds before falling back (two AI steps at a 30 second timeout each).
- In fallback mode the guessed hypotheses can still surface odd results. The study-aid filter matches title phrases only (summary, study guide, SparkNotes, CliffsNotes).
- Author names and titles are shown as Open Library stores them, so they can be in another script.
- Different first-name spellings (for example Fyodor and Fedor) only match when the surname also matches.
- Open Library rate-limits heavy use. The mitigations above reduce the load but cannot prevent a block.
- The AI readiness probes only list models (Gemini model lookup, OpenAI-compatible `GET /models`), so a provider that stalls on real completions still reports ready. A routing model name such as `auto` makes this worse because the upstream model changes per call (see the README).

## Out of scope

LLM re-ranking, persistent cache, ISBN matching, authentication, a hosted live demo.

## Future improvements

- Fail faster when Open Library does not answer (a time budget per lookup phase). Tried and set aside (see `docs/decisions.md`).
- Let a new search or Enter replace a running one.
- Sort title-only searches by edition count so popular works come first.
- Show an English display name for authors stored in another script.
- LLM re-ranking behind a flag, a persistent cache, query logging for tuning.

## Changes from the first draft

- D6: the first draft used `/authors/{id}/works.json` for author-only queries. The build uses `/search.json?sort=editions`.
- D7: the first draft named Gemini only. The build has a provider-neutral AI seam and an OpenAI-compatible client.
- The parser returns hypotheses (the draft returned one). A fifth tier was added. The heuristic parser's guesses rank below exact titles.
- Detail lookups were 8 works in the draft and are 6 in the build, with an author cap, a request limit and a 429 cooldown that the draft did not have.
- New in the build: edition-title and author-alias matching, transliteration-tolerant author names, the year-aware AI explanation facts, the timed progress panel, the theme toggle, example promotion, Docker Compose, and XML documentation.
- New after that: the free-text supplement for thin searches, the Bookshelf UI with `matchTier` and `exactTitle` in the API, and the mobile layout.
- New after that: the health endpoints (the config dump is local-only), the study-aid filter, the "No confident match" message, shareable `?q=` links and saved example chips.
