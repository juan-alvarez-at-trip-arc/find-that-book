# Decision record

Ideas that were tried or considered and not shipped, so they are not retried blindly. The design is in `docs/superpowers/specs/2026-10-08-find-that-book-design.md`.

## Tried and set aside

| Idea | State | Revisit when |
|---|---|---|
| Time budget per Open Library lookup phase (fail faster when Open Library does not answer) | Tried, not kept. The reason was not recorded. Today each attempt times out at 10 s with 2 retries. | Searches hang on a slow Open Library. Measure the slow phase first. |
| Error shown as a balloon under the search box, and the AI notice as a "Basic search" pill with a popover | Prototyped, not kept. The toast was chosen. | The toast needs a rethink, for example when the empty page after a dismissed toast (F4 in `docs/ui-tests/README.md`) is a problem. The balloon keeps the message next to the search box. |
| Error shown as a modal dialog, and the AI notice as a header chip with a tooltip | Prototyped, not kept. A modal is heavy for an error that only needs a retry. | A message that needs a decision from the user. |

## Out of scope by choice

| Idea | Reason |
|---|---|
| LLM re-ranking | Ranking stays deterministic so the order is testable and explanations cannot contradict it. Only reconsider behind a flag. |
| Persistent cache | The one-hour in-memory cache is enough for one instance. |
| ISBN matching | Not in the brief. |
| Hosted live demo | Needs a hosting account. |
| Masking the reader's search text in telemetry | The text is the app's core input, not a credential, and it is needed to debug results. Reconsider if logs leave the operator's own collector. |
| A telemetry backend in `docker-compose.yml` | The app only exports OTLP. Point `OTEL_EXPORTER_OTLP_ENDPOINT` at an existing collector. |
| Docker image build check | Done by hand on 2026-10-10: the image builds and serves the UI and API. No automated test builds or runs the image. |
| Stop button, or an editable search box during a search | The owner kept the box locked and added no Stop (2026-10-10). One search at a time keeps the load on Open Library and the stale-response guard simple. A search can take up to about 43 seconds. |
| A shelf motif on the landing page | The owner kept the landing quiet so the empty space keeps the focus on the search. The bookshelf shows for no results and in the progress stage. |
| A legend for the badges | The owner left the badges explained only by each card's reason sentence. |
| A "Not it?" hint under the results, and closing the gap in the featured card | Reviewed in the critique runs and left as is. |
| A Latin-script author name on the byline | Needs alias handling in the API and changes the "names as stored" rule. Listed in the README under known limitations. |

## Changed after the first draft

| Draft | Built | Why |
|---|---|---|
| Author-only queries use `/authors/{id}/works.json` | `/search.json?sort=editions` | The works endpoint has no popularity order. |
| Gemini only | Provider-neutral AI seam plus an OpenAI-compatible client | Gemini's free tier was unreliable when tested (see the README). |
| One parsed hypothesis | A list of hypotheses | The heuristic parser needs several guesses when no AI is available. |
| 8 detail lookups per search | 6, plus an author cap, a request limit and a 429 cooldown | Open Library rate-limits heavy use. |
| Retry button on the error banner (earlier tried and dropped, reason not recorded) | A "Try again" button that repeats the last search, enabled immediately (now on the error toast) | Accessibility and recovery pass on the UI. It does not wait for a 429 cooldown, so during one it fails fast with the same error. |
| Full-width banners for the error, the AI notice and the weak-match message, and inline text for the empty state | Dismissable toasts in a fixed stack (bottom right, across the bottom on phones) | A toast does not move the layout and keeps Try again next to the message. The first version sat at the top on phones and covered the header and search box, so it moved to the bottom. |
| Titles and names shown as Open Library stores them | The matched edition title is the headline and the stored title sits beneath it ("Catalogued as"). Author names are still shown as stored | A reader who typed an English title saw a Japanese or Swedish one as the answer (`matchedTitle` in the API). |
| The "Basic search mode" toast returns after every search | It shows only next to results and stays dismissed for the page session | It repeated on every search and stacked on the empty-state toast. |
| Neutral blue-grey page, system sans for everything | Warm paper, a serif for the page question and book titles, a ledge under every cover (`DESIGN.md`) | The product's character showed only in the empty and progress states. |
| 260px of page padding while any toast shows | 150px for one toast, 260px for two | A single toast left a large blank area. |
| Static files and API responses served uncompressed | gzip through the ASP.NET response compression middleware (the script goes from about 234 KB to 85 KB) | The seventh critique measured it. HTTPS compression stays off, which is the framework default, and a proxy in front can compress instead. |
| The wordmark is plain text | The wordmark is a link to `/` that reloads the page with no query and an empty search box | A reader needs a way back to a clean start. A plain link does exactly that with no state handling. |
| Progress stage steps advance every 2.5 seconds | Each step has its own time: understand 10 s, search 2 s, match 1 s, explain 30 s (`STEP_MS` in `web/src/lib/progress.ts`) | A logged search took 11.7 s for the AI parse, 1.6 s for Open Library and 29.5 s for the AI explanations, so equal steps showed "Writing explanations" for too short a time. The times are estimates, because the API reports no progress |
