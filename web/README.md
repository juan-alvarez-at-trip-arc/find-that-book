# Find That Book: web UI

React + TypeScript + Vite single-page app for the Find That Book API (see the [root README](../README.md)). A reader types whatever they remember about a book and gets up to 5 ranked Open Library matches, each with an explanation.

## Run it

Requirements: Node 22+. The API must be running for searches to work (`dotnet run --project src/FindThatBook.Api` from the repo root, http://localhost:5080).

```bash
npm install
npm run dev      # http://localhost:5173, /api is proxied to http://localhost:5080
npm test         # Vitest, one run
npm run build    # type-check (tsc -b) and production build into dist/
npm run lint     # Oxlint
npm run preview  # serve the production build locally
```

In production the build in `dist/` is served by the API itself (the Dockerfile copies it to the API's `wwwroot`), so the UI and API share one origin and no CORS setup is needed.

## What it does

- A hero search box with example chips. A search calls `POST /api/search` and shows the results as a bookshelf: covers standing on wooden ledges (4 columns, 2 up to 760px wide, 1 up to 340px), each with a match badge and, below the ledge, title, author and year. The cover links to Open Library. A book without a cover gets a generated cover with its title.
- Badges come from `matchTier` and `exactTitle` in the response (`lib/badge.ts`): "Best match" for the first result with an exact title, "Exact title", "Author match" (author-only matches) and "Close title".
- The explanation shows in a popover on hover or keyboard focus. On narrow screens and touch devices the "Why this book?" button opens it inline under the card.
- Mobile: `viewport-fit=cover` with safe-area padding, tap targets of 36 to 44px, example chips in one horizontally scrolling row, and a header that wraps at 420px.
- While a search runs the previous results, notice and error are cleared and a progress panel shows a gradient bar, bouncing dots, four pips and a status line that advances every 2.5 seconds, then says the search is taking longer than usual. The steps are timed locally, because the API reports no progress. Animations stop under `prefers-reduced-motion`.
- A yellow notice appears when `aiUsed` is false (the AI was unavailable, so results use basic parsing), and a blue information notice shows the server message above the results when the best match is weak. Both are `role="status"`. The empty state shows the server's message and a tip to try the author's last name or fewer words. Failures show a red banner with the API's problem-details title, or a generic message when the request itself failed, plus a "Try again" button that repeats the last search.
- Results heading: when results arrive, focus moves to an `h2` ("N books for “query”"). Card titles are `h3`.
- Escape closes an open explanation and keeps the hover popover hidden until the pointer or focus leaves the card.
- While a search runs, four placeholder books appear on the shelf under the progress panel. After the first search the hero shrinks (smaller heading, no lead text, chips in one scrolling row).
- A late response from an older search is ignored, so results never show for the wrong query. The Search button is disabled while a search runs, so pressing Enter during a search does nothing.
- Shareable link: a search puts its text in the page URL as `?q=...` (without adding history entries), and opening a URL with `q` fills the search box and runs it once.
- Example chips: five at start. A search that returns at least one result moves its text to the front of the list (an existing chip is moved, otherwise the last chip is dropped). Failed and empty searches leave the list alone. The list is saved in `localStorage` under `ftb-examples` (guarded access, invalid data falls back to the defaults).
- Theme toggle: System (default), Light or Dark. System follows the operating system live. The choice is stored in `localStorage` under `ftb-theme`, every storage access is guarded, and a small inline script in `index.html` applies a saved Light or Dark choice before the first paint to avoid a flash. The theme is applied through a `data-theme` attribute on `<html>` and `color-scheme` in CSS, and the colors are CSS variables defined for each mode in `App.css`.

## Project layout

```
src/
  main.tsx                 entry point (loaded by index.html)
  setupTests.ts            Testing Library and jest-dom setup (referenced by vite.config.ts)
  components/
    App/                   App.tsx, App.css (all styles, theme tokens, shelf and mobile rules), App.test.tsx
    ResultCard/            ResultCard.tsx (one book on the shelf)
    SearchProgress/        SearchProgress.tsx, SearchProgress.test.tsx
    ShelfSkeleton/         ShelfSkeleton.tsx (placeholder books while searching)
    ThemeToggle/           ThemeToggle.tsx, ThemeToggle.test.tsx
  lib/
    api.ts                 fetch wrapper for POST /api/search, turns problem details into errors
    types.ts               response types (the API uses first_publish_year in snake_case)
    badge.ts               badgeFor: picks the badge from tier, exact title and rank
    theme.ts               theme modes, guarded storage, applying the data-theme attribute
    examples.ts            promoteExample, the example-list rule
    examples.test.ts
```

Each component lives in its own folder with its tests. `lib/` holds plain TypeScript modules that are not components.

## Tests

53 tests, Vitest with jsdom and Testing Library, no network access (`fetch` is stubbed). The shelf layout and mobile styles are checked by hand, not by tests.

- `App`: results with explanations and badges, the "Why this book?" toggle and Escape, the focused results heading, notice roles and styles, the empty message and tip, problem-details errors, generic network errors, Try again, example chips (running a search, promotion, no change on failure or empty results), clearing old results during a search, the progress panel, the skeleton, the compact hero, the shelf slot, blank input.
- `SearchProgress`: step order with fake timers, the "longer than usual" message, pip states, timer cleanup.
- `ThemeToggle`: default System mode, choosing and persisting a mode, restoring a saved mode, ignoring an invalid value, blocked storage, the accessible group label.
- `examples`: the promotion rule (new query, existing query, case and spaces, stable length, blank input, no mutation) and saving and restoring the list (invalid data, blocked storage).

## Notes

- Tooling: Vite with `@vitejs/plugin-react`, TypeScript, Vitest, Oxlint. The React Compiler is not enabled.
- Type-aware lint rules are available by installing `oxlint-tsgolint` and setting `"typeAware": true` in `.oxlintrc.json`.
- Known quirk: while a search is running, Enter does nothing because the Search button is disabled. A possible improvement is letting a new search replace the running one.
