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
| Docker image build check | The Docker daemon was not available when the project was built. |

## Changed after the first draft

| Draft | Built | Why |
|---|---|---|
| Author-only queries use `/authors/{id}/works.json` | `/search.json?sort=editions` | The works endpoint has no popularity order. |
| Gemini only | Provider-neutral AI seam plus an OpenAI-compatible client | Gemini's free tier was unreliable when tested (see the README). |
| One parsed hypothesis | A list of hypotheses | The heuristic parser needs several guesses when no AI is available. |
| 8 detail lookups per search | 6, plus an author cap, a request limit and a 429 cooldown | Open Library rate-limits heavy use. |
| Retry button on the error banner (earlier tried and dropped, reason not recorded) | A "Try again" button that repeats the last search, enabled immediately (now on the error toast) | Accessibility and recovery pass on the UI. It does not wait for a 429 cooldown, so during one it fails fast with the same error. |
| Full-width banners for the error, the AI notice and the weak-match message, and inline text for the empty state | Dismissable toasts in a fixed stack (bottom right, across the bottom on phones) | A toast does not move the layout and keeps Try again next to the message. The first version sat at the top on phones and covered the header and search box, so it moved to the bottom. |
