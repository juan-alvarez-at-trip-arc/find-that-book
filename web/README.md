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

- A hero search box with example chips. A search calls `POST /api/search` and shows the top match large (cover, badge, title, author, year, its reason and a "View on Open Library" link) and the other matches under "Other possible matches" as compact cards (2 columns, 1 up to 760px wide). Covers link to Open Library. A book without a cover gets a generated cover with its title, and the same cover shows while an image loads or if it fails. Every cover stands on a wooden ledge. The headline is the matched edition title when the work is titled differently, with a "Catalogued as" line beneath it.
- Badges come from `matchTier` and `exactTitle` in the response (`lib/badge.ts`): "Best match" for the first result with an exact title, "Exact title", "Author match" (author-only matches) and "Close title".
- The top match always shows its explanation. The other cards have a native "Why this book?" disclosure (`<details>`), collapsed by default.
- Mobile: `viewport-fit=cover` with safe-area padding, tap targets of at least 24px at every width (36 to 44px on phones), example chips in one horizontally scrolling row, and a header that wraps at 420px.
- While a search runs the previous results and toasts are cleared and a progress stage replaces the results area. It shows an animated scene for the current step (a magnifier over the query, a library with radar rings, clues linked to book spines, a quill writing lines), the step title and a detail line, and four step chips that go from pending to active to done. Each step has its own time (understand 10 s, search 2 s, match 1 s, explain 30 s), then the stage says the search is taking longer than usual. The steps are timed locally, because the API reports no progress. The scenes are static under `prefers-reduced-motion`.
- Messages are toasts (`components/Toast/`), stacked in a fixed container at the bottom right (across the bottom on phones, up to 600px wide). Each has a title, a message and a × button. A dismissed toast comes back after the next search, except the "Basic search mode" toast, which stays dismissed for the page session and only shows next to results. A failure shows a red "Search failed" toast (`role="alert"`) with the API's problem-details title, or a generic message when the request itself failed, plus a "Try again" button that repeats the last search. A yellow "Basic search mode" toast appears when `aiUsed` is false (the AI was unavailable, so results use basic parsing). A blue "Closest results" toast shows the server message next to the results when the best match is weak. With no results a blue toast titled "No matching books found." shows a tip to try the author's last name or fewer words. The yellow and blue toasts are `role="status"`. While any toast shows, the page gets extra bottom padding (`.page:has(.toast)`, 150px, or 260px when two toasts stack) so the last result is not hidden behind the stack.
- Results heading: when results arrive, focus moves to an `h2` ("N books for “query”"). The top match title and the "Other possible matches" label are `h3`, and the titles of the other cards are `h4`. After an error or an empty result focus moves to the search box. The page title becomes "N books for “query” · Find That Book".
- With no results, an empty bookshelf picture (`components/EmptyShelf/`, an inline SVG with `role="img"`) fills the area under the search box, next to the empty-state toast. It stays after the toast is dismissed. After the first search the hero shrinks (smaller heading, no lead text, chips in one scrolling row).
- A late response from an older search is ignored, so results never show for the wrong query. The search box, the example chips and the Search button are disabled while a search runs, so pressing Enter during a search does nothing.
- The logo is a link to `/`: clicking it reloads the page with no `q` parameter and an empty search box.
- Shareable link: a search puts its text in the page URL as `?q=...` (without adding history entries), and opening a URL with `q` fills the search box and runs it once.
- Look: warm paper page, a serif (system stack: Iowan Old Style, Palatino, Georgia) only for the page question and book titles, one blue accent and spine colors that mean match quality. The product context is in `PRODUCT.md` and the visual system in `DESIGN.md`, both at the repo root.
- Page metadata in `index.html`: description, theme-color (matches the page backgrounds, checked by a test), Open Graph and Twitter tags, JSON-LD, a noscript line. `header` sits outside `main`, and the search form has `role="search"`.
- Example chips: five at start. A search that returns at least one result moves its text to the front of the list (an existing chip is moved, otherwise the last chip is dropped). Failed and empty searches leave the list alone. The list is saved in `localStorage` under `ftb-examples` (guarded access, invalid data falls back to the defaults).
- Theme toggle: System (default), Light or Dark. System follows the operating system live. The choice is stored in `localStorage` under `ftb-theme`, every storage access is guarded, and a small inline script in `index.html` applies a saved Light or Dark choice before the first paint to avoid a flash. The theme is applied through a `data-theme` attribute on `<html>` and `color-scheme` in CSS, and the colors are CSS variables defined for each mode in `App.css`.

## Project layout

```
src/
  main.tsx                 entry point (loaded by index.html)
  setupTests.ts            Testing Library and jest-dom setup (referenced by vite.config.ts)
  components/
    App/                   App.tsx, App.css (all styles, theme tokens, featured results and mobile rules), App.test.tsx
    FeaturedResults/       FeaturedResults.tsx, FeaturedResults.test.tsx (top match and the other matches)
    EmptyShelf/            EmptyShelf.tsx (empty bookshelf picture for a search with no results)
    SearchProgress/        SearchProgress.tsx, Scenes.tsx (the four animated step scenes), SearchProgress.test.tsx
    ThemeToggle/           ThemeToggle.tsx, ThemeToggle.test.tsx
    Toast/                 Toast.tsx, Toast.css, Toast.test.tsx (error, notice and info messages)
  lib/
    api.ts                 fetch wrapper for POST /api/search, turns problem details into errors
    types.ts               response types (the API uses first_publish_year in snake_case)
    progress.ts            STEP_MS, how long each progress step is shown (10, 2, 1 and 30 seconds)
    badge.ts               badgeFor: picks the badge from tier, exact title and rank
    theme.ts               theme modes, guarded storage, applying the data-theme attribute
    examples.ts            promoteExample, the example-list rule
    examples.test.ts
```

Each component lives in its own folder with its tests. `lib/` holds plain TypeScript modules that are not components.

## Tests

88 tests, Vitest with jsdom and Testing Library, no network access (`fetch` is stubbed). The layout and mobile styles are checked by hand, not by tests.

- `FeaturedResults`: the edition title headline, one featured link with a new-tab cue, the cover fallback, the top match with its reason visible, the other matches with a badge and a collapsed reason, a single result, the placeholder cover and unknown author, the cover image, the generated cover when an image fails, and the edition title as the headline with its "Catalogued as" line.
- `App`: the logo link to `/`, focus returning to the search box, the page title, results with explanations and badges, the focused results heading, the toasts (roles, styles, dismissing each one and its return after a new search, the weak-match and empty-state messages), problem-details errors, generic network errors, Try again, example chips (running a search, promotion, no change on failure or empty results), clearing old results during a search, the progress stage, the empty bookshelf (shown for an empty search, also after dismissing the toast, never with results or an error), the compact hero, blank input, focus returning to the search box after an error or an empty result, the page title, the notice staying dismissed, and one piece of advice in the empty state.
- `themeColor`: the two `theme-color` metas in `index.html` match the light and dark page backgrounds in `App.css` (Vitest is set up to process `App.css`, so the test can read it).
- `Toast`: icon marks take their color from the theme, not a literal, plus role by kind (alert for errors, status for notice and info), the title and message, the dismiss button and an optional action.
- `SearchProgress`: step order with fake timers, the "longer than usual" message, step chip states, timer cleanup.
- `ThemeToggle`: default System mode, choosing and persisting a mode, restoring a saved mode, ignoring an invalid value, blocked storage, the accessible group label.
- `examples`: the promotion rule (new query, existing query, case and spaces, stable length, blank input, no mutation) and saving and restoring the list (invalid data, blocked storage).

## Notes

- Tooling: Vite with `@vitejs/plugin-react`, TypeScript, Vitest, Oxlint. The React Compiler is not enabled.
- Type-aware lint rules are available by installing `oxlint-tsgolint` and setting `"typeAware": true` in `.oxlintrc.json`.
- Known quirk: while a search is running, Enter does nothing because the search box and the Search button are disabled. A possible improvement is letting a new search replace the running one.
