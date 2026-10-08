# Find That Book Implementation Plan

> **Status: as built.** Tasks 1-9 are the original plan. Tasks 10-16 record work added during and after execution. The repository is the source of truth for code, so this document lists, for each task, the final files, interfaces, behavior, tests and the commit that delivered it. Original code listings that no longer match the code were removed. If this plan is ever re-executed, use `superpowers:executing-plans` or `superpowers:subagent-driven-development` and the steps below as the checklist.

**Goal:** Build a .NET 8 Web API plus React UI that turns a messy text query into up to 5 ranked Open Library matches, each with a one-sentence explanation.

**Architecture:** Layered .NET solution. `Application` holds the pure matching logic, normalization, fallbacks and interfaces. `Infrastructure` holds the Open Library client and the AI clients (Gemini or any OpenAI-compatible endpoint, chosen by configuration). `Api` hosts the controller and serves the built React app. Deterministic code ranks. The AI extracts query hypotheses and writes explanations, and every AI step has an AI-free fallback that sets `aiUsed: false`.

**Tech Stack:** .NET 8, ASP.NET Core controllers, xUnit, `WebApplicationFactory`, React + Vite + TypeScript, Vitest + Testing Library, Docker and Docker Compose, Gemini REST API, OpenAI-compatible chat completions, Open Library API.

**Spec:** `docs/superpowers/specs/2026-10-08-find-that-book-design.md` (brief: `docs/2025_Developer_Project.md`)

**Branch:** `feature/find-that-book`, last commit `4393dfe`. Final test counts: 196 .NET tests, 31 frontend tests, all passing.

## Global Constraints

- Target `net8.0` only. `global.json` pins SDK `8.0.100` with `rollForward: latestFeature`.
- Layout: `src/FindThatBook.Application`, `src/FindThatBook.Infrastructure`, `src/FindThatBook.Api`, `tests/FindThatBook.Tests`, `web/` (frontend source in `web/src/components/<Name>/` and `web/src/lib/`).
- Endpoint: `POST /api/search` with `{ "query": string }`. `GET /api/health` returns 200.
- Response fields: `aiUsed`, `message`, `results[]` with `title`, `author`, `authors`, `first_publish_year`, `workId`, `url`, `coverUrl`, `explanation`. Only `first_publish_year` is snake_case.
- Max 5 results. Queries are trimmed and truncated to 200 characters. A blank query returns 400.
- Errors use RFC 7807 problem details: 400, 502 (Open Library cannot be searched), 500. No stack traces.
- Open Library: 10 s per attempt, 2 retries on 5xx and timeouts, 1 hour in-memory cache (size-limited), 6 detail lookups per search, 3 authors per work, 2 concurrent requests, HTTP 429 never retried and followed by a cooldown, optional contact in the `User-Agent`.
- AI: provider chosen by `Ai:Provider` (`Gemini` default, or `OpenAiCompatible`). The app must run with no key (fallback mode). Both AI HTTP clients time out at 30 s.
- No test calls a live API.
- Production code under `src/` carries XML documentation comments, with no comments inside method bodies added.
- Commit messages carry no co-author trailer (the user's global instruction overrides the default attribution).

## Plan-level decisions (all accepted)

1. **Tier 5 `TitleOnly`** covers a title match with no matching author, because the original four tiers left title-only queries unranked.
2. **Parser returns hypotheses.** The AI returns one. The heuristic parser returns up to six, all flagged `Guessed`. The catalog searches each and the matcher scores every candidate against all of them.
3. **No `/authors/{id}/works.json`.** Author-only queries use `/search.json?sort=editions`.
4. **Gemini default model `gemini-3.8-flash`.** The first draft's `gemini-2.5-flash` returned 404 ("no longer available to new users") on the project's key.
5. **Provider-neutral AI seam** with a Gemini client and an OpenAI-compatible client, selected by configuration.
6. **Guessed hypotheses rank below an exact title alone**, while an author the AI extracted keeps its strong ranking.
7. **Edition titles and author aliases** are used for matching, and author names are compared through transliteration folding.

## Review Focus

Failure modes the feature tasks would not otherwise exercise. All have tests.

1. A query with no words after noise removal (`1937`, `???`) returns 200 with an empty list and a message, and makes no Open Library call. (Tasks 4, 5, 7)
2. Diacritics and non-English names match their plain forms. (Tasks 2, 3, 14)
3. Open Library docs missing optional fields, wrongly typed fields, and `/works` returning 404 or 500, do not crash and rank lower. (Tasks 3, 5)
4. Queries over 200 characters are truncated. (Task 7)
5. The AI returns malformed JSON, both fields null, a short explanation array or a non-string payload, and the request still succeeds with `aiUsed: false`. (Tasks 6, 7, 11)

---

### Task 1: Solution scaffold

- [x] Commit `a2b12ad`.

**Files:** `global.json`, `FindThatBook.sln`, `.gitignore`, the four .NET projects, `src/FindThatBook.Api/Properties/launchSettings.json` (fixed port 5080).

**Produces:** a building solution. References: `Infrastructure` to `Application`, `Api` to both, `Tests` to all three. Packages: logging abstractions, `Microsoft.Extensions.Http`, memory cache and options configuration extensions (Infrastructure), `Microsoft.AspNetCore.Mvc.Testing` (Tests).

**Verified by:** `dotnet build`.

### Task 2: Normalizer

- [x] Commit `e0aa155`. Extended in `287ec69` (apostrophes, full title key) and `4393dfe` (name tokens).

**Files:** `src/FindThatBook.Application/Text/Normalizer.cs`, `tests/FindThatBook.Tests/NormalizerTests.cs`.

**Produces (all static):** `Normalize` (lowercase, strip diacritics, drop apostrophes, other punctuation becomes a space), `StripSubtitle` (cuts at `:`, ` - `, ` – `, `, or `, removes parentheticals), `TitleKey` (subtitle and leading article removed), `FullTitleKey` (subtitle kept, used for dedupe), `NormalizeName` (merges single-letter initials), `NameTokens` (name tokens with transliteration folding: `y` and `j` to `i`, `tch` to `ch`, `ie` to `e`, trailing `ii` to `i`).

**Tests:** diacritics, apostrophes, subtitles, articles, initials, transliteration pairs that must be equal (Dostoevsky, Dostoyevsky, Dostoievski; Tolstoy, Tolstoi; Tchaikovsky, Chaikovsky) and pairs that must stay apart (Tolkien and Tolstoy, Austen and Austin).

### Task 3: Contracts and Matcher

- [x] Commit `86da178`. Changed in `287ec69`, `9b423c9` and `4393dfe`.

**Files:** `Models/Models.cs`, `Abstractions/Abstractions.cs`, `Matching/Matcher.cs`, `tests/.../MatcherTests.cs`, `tests/.../Support/Fakes.cs`.

**Produces (final shapes):**
- `ParsedQuery(Title?, Author?, Keywords, Guessed = false)`.
- `CatalogCandidate(WorkId, Title, PrimaryAuthors, Contributors, FirstPublishYear?, EditionCount, CoverId?, Enriched, AlternativeTitles, PrimaryAuthorAliases)`.
- `MatchTier` 1-5, `AuthorMatchKind`, `MatchedBook(Book, Tier, ExactTitle, AuthorMatch, MatchedAuthor?, YearMatches, FromGuess, MatchedTitle?)`.
- `ParseOutcome`, `ExplanationOutcome`, `SearchRequest`, `BookResult`, `SearchResponse`.
- `IQueryParser`, `IBookCatalog`, `IExplanationWriter`, `ISearchService`, `AiUnavailableException`, `CatalogUnavailableException`.
- `Matcher.Rank(hypotheses, candidates, max = 5)`.

**Behavior:** each candidate keeps its best match across hypotheses. Title match uses the work title, then edition titles (exact is equal keys, near is word containment or Jaccard of at least 0.6). Author match requires every query token to equal or prefix a canonical name token, checking primary authors, then aliases, then contributors. Sort strength (lower first): tier 1, tier 2, tier 3 from the AI, tier 4, tier 5 exact, tier 3 guessed, tier 5 near. Ties break on exact title, year keyword, edition count, title. Dedupe by full title and first primary author.

**Tests:** every tier, subtitles, contributors, unenriched candidates, dedupe, year tie-break, best hypothesis, result cap, guessed versus AI ranking, edition titles, aliases, transliteration, series books not collapsed.

### Task 4: Heuristic parser and template explanations

- [x] Commit `4ce10f9`. Changed in `287ec69`, `9b423c9`.

**Files:** `Parsing/HeuristicQueryParser.cs`, `Explanations/TemplateExplanationWriter.cs`, their tests.

**Behavior:** the parser turns noise words and 1500-2099 years into keywords and emits up to six `Guessed` hypotheses (whole text as title, as author, first or last word as author, first two or last two words as author). No words left means no hypotheses. `TemplateExplanationWriter.Describe` writes one sentence from tier, matched author, contributors, year match, enrichment state and, when relevant, the matched edition title.

### Task 5: Open Library catalog

- [x] Commit `da4a11e`. Changed in `287ec69`, `9b423c9` and `864364d` (see Tasks 10, 13 and 14).

**Files:** `OpenLibrary/OpenLibraryCatalog.cs`, `OpenLibraryOptions.cs`, `OpenLibraryGate.cs`, `tests/.../OpenLibraryCatalogTests.cs`, `OpenLibraryLoadTests.cs`.

**Behavior (final):** hypotheses are searched in parallel (`limit=5`, `fields` includes `editions`, `sort=editions` without a title, one free-text retry when a fielded search finds nothing). Up to `MaxDetailLookups` works are chosen round-robin across hypotheses and enriched four at a time. Authors come from the work record (first `MaxAuthorsPerWork`), with the author record's `name` for display and `personal_name` plus `alternate_names` as aliases. Edition titles from the search become `AlternativeTitles`. Tolerant parsing skips docs with wrongly typed fields. A failed lookup yields an unenriched candidate. Retries apply to 5xx, timeouts and network errors. A 429 starts a shared cooldown and is never retried.

### Task 6: Gemini clients and fallbacks (original), reworked in Task 11

- [x] Commit `a077992`.

**Files (final):** `Infrastructure/Ai/*`, `Infrastructure/Gemini/*`, `Application/Parsing/FallbackQueryParser.cs`, `Application/Explanations/FallbackExplanationWriter.cs`, `tests/.../GeminiTests.cs`, `FallbackTests.cs`.

**Behavior:** the AI parser and writer validate the reply and throw `AiUnavailableException` for malformed JSON, empty fields, missing indexes or non-string payloads. The decorators catch only that exception and delegate to the heuristic parser or template writer. A test found and fixed an `ArgumentOutOfRangeException` on an empty `candidates` array in the Gemini reply.

### Task 7: SearchService and API

- [x] Commit `3ee1116`.

**Files:** `Search/SearchService.cs`, `Api/Controllers/SearchController.cs`, `Api/ApiExceptionHandler.cs`, `Api/Program.cs`, `Infrastructure/ServiceCollectionExtensions.cs`, `appsettings.json`, `tests/.../SearchServiceTests.cs`, `ApiTests.cs`.

**Behavior:** trim and truncate the query, parse, search, rank, explain, build the response (`aiUsed` is the parser's and the writer's flags combined, no results gives a message). Blank query 400, `CatalogUnavailableException` 502, anything else 500, all as problem details. `GET /api/health`. Static files and a fallback to `index.html`.

**Verified live:** a search against the real Open Library with no AI key returned The Hobbit first.

### Task 8: React frontend (original), extended in Tasks 12 and 16

- [x] Commit `1155695`.

**Behavior:** search box, example chips, result cards, empty state, error banner and a notice when `aiUsed` is false, with a stale-response guard. Vite proxy to `localhost:5080` in development, Vitest and Testing Library for tests.

### Task 9: Docker, README and Compose

- [x] Commits `7689e66`, `6918bd7`, `24dde1e`.

**Files:** `Dockerfile` (node build, .NET publish, aspnet runtime on port 8080), `.dockerignore`, `docker-compose.yml`, `README.md`, `.gitignore` (adds `.env`).

**Behavior:** Compose maps `.env` variables (`AI_PROVIDER`, `GEMINI_*`, `OPENAI_*`, `OPENLIBRARY_*`) to the app settings. **Not verified:** the image was never built (the Docker daemon was not running); the equivalent layout was run by hand and `docker compose config -q` accepts the file.

### Task 10: Final review fix pass

- [x] Commit `287ec69`.

Fixes from the whole-branch review, each with a test that failed first: apostrophes no longer split words ("enders game"), dedupe keeps series books apart (full title key), searches run in parallel and detail lookups are taken round-robin across hypotheses, the memory cache is size-limited, wrongly typed upstream values no longer cause a 500, and the heuristic parser also tries the last two words as the author.

### Task 11: Provider-neutral AI and the OpenAI-compatible client

- [x] Commits `c09a59a`, `59b1066`, `d456013`, `a6e0057`.

**Files:** `Ai/IAiClient.cs`, `Ai/AiSchemas.cs`, `Ai/AiQueryParser.cs`, `Ai/AiExplanationWriter.cs`, `Gemini/GeminiClient.cs`, `GeminiSchema.cs`, `GeminiOptions.cs`, `OpenAi/OpenAiCompatibleClient.cs`, `OpenAiCompatibleOptions.cs`, tests `GeminiSchemaTests`, `OpenAiCompatibleClientTests`, `AiProviderSelectionTests`.

**Behavior:**
- `IAiClient.GenerateJsonAsync(prompt, schema, ct)` is the single seam. Schemas are standard JSON Schema. `GeminiSchema.FromJsonSchema` converts them to Gemini's dialect (a test asserts the result equals the original Gemini schemas).
- `Ai:Provider` selects the client at registration (blank or `Gemini` default, `OpenAiCompatible`, anything else throws a clear error).
- The OpenAI-compatible client uses JSON mode, `stream: false` (some gateways stream by default), temperature 0, an optional Bearer key, and wraps non-object schemas as `{"result": ...}`.
- Gemini default model `gemini-3.8-flash`, a blank model uses the default, timeouts 30 s.
- The explanation prompt receives the publication year only when it matched the query, because a first attempt that merely sent a flag made the model state false year matches.

**Verified live:** the OpenAI-compatible client against a local gateway (`aiUsed: true`, correct explanations). Gemini calls were limited by provider capacity during testing, so its full path was not confirmed end to end.

### Task 12: UI features

- [x] Commits `d44a588`, `9b55c06`, `0946638`, `0b45260`.

- Clear previous results, notice and error when a new search starts.
- `SearchProgress`: gradient bar, dots, four pips and a status line advancing every 2.5 s, then a "taking longer than usual" message. Honors `prefers-reduced-motion`.
- `ThemeToggle` and `lib/theme.ts`: System (default), Light, Dark, stored under `ftb-theme` with guarded storage access, applied through `data-theme` plus `color-scheme`, with an inline pre-paint script in `index.html`.
- `lib/examples.ts` (`promoteExample`): a search with results moves to the front of the examples, an existing chip is moved, the last is dropped otherwise, failures and empty searches change nothing.

**Tests:** `SearchProgress.test.tsx`, `ThemeToggle.test.tsx`, `lib/examples.test.ts`, `App.test.tsx`.

### Task 13: Open Library load reduction

- [x] Commits `864364d`, `24dde1e`.

After a test run produced HTTP 429 on about 36% of Open Library responses (and then hung connections), the client was changed to: 6 detail lookups per search (was 12), 3 authors per work, a process-wide gate of 2 concurrent requests (`OpenLibraryGate`), no retry on 429 plus a cooldown from `Retry-After` or 30 s during which requests fail fast and cached lookups still work, and an optional `OpenLibrary:Contact` in the `User-Agent`. Settings are in `appsettings.json` and Compose.

**Verified:** against a local fake Open Library (30 requests for the heaviest search, at most 2 at once) and a later 10-query run against the real service with all 131 responses 200.

### Task 14: Matching quality

- [x] Commits `9b423c9`, `4393dfe`.

- **Guessed versus extracted authors:** `ParsedQuery.Guessed` and the strength order in Task 3 stop a guessed author split from beating an exact title ("the little prince").
- **Non-English work titles and authors:** the catalog asks for matching edition titles and reads author aliases, and the matcher uses both ("kafka on the shore" finds the work titled in Japanese). Explanations mention a match through an edition title.
- **Transliteration:** author names are compared through `Normalizer.NameTokens`, so spelling variants match ("dostoevsky" and "Dostoievski").

### Task 15: XML documentation comments

- [x] Commit `9a9cac2`.

All classes, records, interfaces, enums (with members), methods, constructors and properties under `src/` have XML docs. Verified: the diff changed only `///` lines (plus one enum expanded to several lines), a documentation build produced no warnings, and an undocumented member did trigger `CS1591`, so the check is real.

### Task 16: Frontend reorganization

- [x] Commit `a5bd389`.

`web/src/components/<Name>/` for `App`, `ResultCard`, `SearchProgress` and `ThemeToggle` with their tests and `App.css`, and `web/src/lib/` for `api.ts`, `types.ts`, `theme.ts` and `examples.ts`. `main.tsx` and `setupTests.ts` stay at the root of `src/`.

---

## Not done or deferred

- A hosted live demo (needs a hosting account).
- Building the Docker image (the daemon was not available).
- Ideas tried and set aside: a time budget per Open Library lookup phase (fail faster), and a Retry button on the error banner.
- Open items are listed in the spec under "Known limitations" and "Future improvements".
