# Find That Book

.NET 8 Web API plus React UI. A messy text query becomes up to 5 ranked Open Library matches, each with a one-sentence explanation. The AI (Gemini or any OpenAI-compatible endpoint) only extracts the query and writes explanations. Ranking is deterministic code. Docs: `README.md`, `web/README.md`, `docs/superpowers/specs/` (backend design), `docs/ui-proposals/ui-spec.md` (UI), `docs/architecture/` (diagrams), `docs/ui-tests/README.md` (by-hand UI test results).

## Commands

- Backend tests: `dotnet test`. Stop the running API first, it locks the DLLs.
- Run the API: `dotnet run --project src/FindThatBook.Api` (http://localhost:5080). Pass settings as env vars (`Ai__Provider`, `Gemini__ApiKey`, `OpenAiCompatible__*`, `OpenLibrary__Contact`). `dotnet run` does not read `.env`, only `docker compose` does.
- Frontend (from `web/`): `npm test`, `npm run lint` (Oxlint), `npm run build` (`tsc -b` and Vite), `npm run dev` (http://localhost:5173, proxies `/api` to 5080). `vite.config.ts` makes Vitest process `App.css` (other CSS is stubbed), so a test can read it with `?raw`.
- Serve the built UI from the API: `npx vite build --outDir ../src/FindThatBook.Api/wwwroot --emptyOutDir` from `web/`. `wwwroot` is git-ignored.
- Docker: `docker compose up --build` (http://localhost:8080). The image was built and run by hand on 2026-10-10.

## Layout

- `src/FindThatBook.Application`: models, interfaces, `Normalizer`, `Matcher`, heuristic parser, template explanations, fallback decorators, `SearchService`, `Telemetry/FindThatBookTelemetry` (trace source and metrics). No I/O.
- `src/FindThatBook.Infrastructure`: Open Library client (`OpenLibraryCatalog`, `OpenLibraryGate`), AI seam (`IAiClient`), Gemini and OpenAI-compatible clients, `Telemetry/` (`HttpLoggingHandler`, `SensitiveDataMasker`), DI registration.
- `src/FindThatBook.Api`: controller, problem details, static files, `appsettings.json`, `TelemetryExtensions` (OpenTelemetry setup).
- `tests/FindThatBook.Tests`: xUnit with stub `HttpMessageHandler`s, `FakeAiClient`, `FakeCatalog` (`Support/Fakes.cs`).
- `web/src/components/<Name>/` (component, CSS and tests together) and `web/src/lib/` (plain modules).

## Rules for changes

- Ranking stays deterministic. Do not add LLM re-ranking.
- Every AI step must fall back to the non-AI version and set `aiUsed: false`. Decorators catch only `AiUnavailableException`.
- Strength order, lower is stronger: T1=10, T2=20, T3 AI=30, T4=40, T5 exact=50, T3 guessed=60, T5 near=70. Change it only with a matcher test.
- Be gentle with Open Library: 6 detail lookups per search, 2 concurrent requests, a 429 is never retried and starts a cooldown. Do not add calls without checking `OpenLibraryLoadTests`.
- Telemetry is off unless `Telemetry:Enabled` or `OTEL_EXPORTER_OTLP_ENDPOINT` is set. Logged HTTP headers, URLs and bodies must pass through `SensitiveDataMasker`. A new credential header, query parameter or JSON field name needs a masker test first. New spans and metrics go through `FindThatBookTelemetry`.
- Production code under `src/` carries XML doc comments on every class, method, property, enum and interface, and no comments inside method bodies. Tests and `web/` follow the surrounding style.
- No test calls a live API. Add a test that fails first for each fix.
- The API field `first_publish_year` is the only snake_case field, matching the brief.
- Keep colors in the CSS variables in `App.css`, and check light, dark and a 390px width after UI changes.
- Messages to the user (errors, notices, the weak-match message, the empty state) are `Toast`s from `web/src/components/Toast/`, rendered in the `.toasts` container in `App.tsx`. Do not add inline banners. A search with no results also shows the `EmptyShelf` picture (`web/src/components/EmptyShelf/`), which carries no text. Toast colors are `--danger`, `--warn` and `--accent`. While a toast shows, `.page:has(.toast)` reserves bottom padding, so after adding one check that nothing is covered at the end of the page.

## Working agreements

- `.env` holds real secrets and is git-ignored. Never print or commit its values. `.env.sample` holds placeholders only.
- Commit only when asked, and only the files named or staged. No co-author trailer in commit messages.
- The default branch is `master` (there is no `main`).
- Use the Write and Edit tools for files. Long bash heredocs with quotes have failed in this environment.
- Chrome testing: the search box, the example chips and the Search button are disabled while a search runs, so wait for them to re-enable before the next query. Open Library and the AI gateway can time out, so check both before judging results.
