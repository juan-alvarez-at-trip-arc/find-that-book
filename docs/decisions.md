# Decision record

Ideas that were tried or considered and not shipped, so they are not retried blindly. The design is in `docs/superpowers/specs/2026-10-08-find-that-book-design.md`.

## Tried and set aside

| Idea | State | Revisit when |
|---|---|---|
| Time budget per Open Library lookup phase (fail faster when Open Library does not answer) | Tried, not kept. The reason was not recorded. Today each attempt times out at 10 s with 2 retries. | Searches hang on a slow Open Library. Measure the slow phase first. |

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
| Retry button on the error banner (earlier tried and dropped, reason not recorded) | A "Try again" button that repeats the last search, enabled immediately | Accessibility and recovery pass on the UI. It does not wait for a 429 cooldown, so during one it fails fast with the same error. |
