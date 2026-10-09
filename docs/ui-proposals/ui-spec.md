# Find That Book: UI Spec

Scope: every UI decision, the three design proposals, and what was implemented. The backend design is in `docs/superpowers/specs/2026-10-08-find-that-book-design.md`.

Status: Bookshelf (proposal 2) and the mobile layout are implemented and merged into `master` (commits `fc9615b`, `2eb23df`, `5adc586`, docs in `99e3f85`). The accessibility and flow pass (D13 to D18) is in `659cc63`, also merged into `master`. The results layout was then changed from the shelf to a featured top match plus compact cards (D20), which supersedes the shelf-specific parts of D9, D10, D13, D17 and D18. Sections 3.2 to 3.4 describe the featured layout. The error, AI-unavailable, weak-match and empty-state messages were then changed from banners and inline text to toasts (D21), which supersedes the banner parts of D14 to D16. The progress panel and the results skeleton were then replaced by one illustrated progress stage (D22), which supersedes D4 and the placeholder part of D17, and the empty state got a bookshelf picture (D23).

## 1. Decisions

| # | Decision | Why |
|---|----------|-----|
| D1 | React + Vite + TypeScript, served by the API in one container | One deployment, no CORS |
| D2 | Source split into `web/src/components/<Name>/` and `web/src/lib/` | Each component has its own folder and tests |
| D3 | Theme: System (default), Light, Dark through `data-theme` on `<html>`, stored under `ftb-theme`, applied by an inline script before first paint | No flash of the wrong theme, follows the OS |
| D4 | A timed progress panel while searching (the API reports no progress). Replaced by the progress stage (D22) | Searches take several seconds |
| D5 | Example chips promote successful searches and are saved in `localStorage` | Shows what works and keeps it between visits |
| D6 | A late response from an older search is ignored | Results never show for the wrong query |
| D7 | Design direction: Bookshelf (proposal 2) | The task is "is this the book I remember?", and large covers answer that fastest |
| D8 | The API returns `matchTier` and `exactTitle` per result | Badges need the match tier, and parsing the explanation text would be fragile |
| D9 | The explanation is hidden by default and shown on hover, focus or tap | Keeps the shelf scannable |
| D10 | On narrow screens and touch devices the explanation opens inline, not as a popover | A popover overlapped neighbouring cards and was too narrow in 2 columns |
| D11 | Colors are CSS variables defined per theme in `App.css` | One place to change the palette, used by the shelf, progress stage and toggle |
| D12 | The three proposals stay in the repo as static mockups | They document the alternatives |
| D13 | Escape closes the explanation and keeps the hover popover dismissed until the pointer or focus leaves the card | WCAG 1.4.13: hover content must be dismissable and hoverable |
| D14 | After a search with results, focus moves to a heading ("N books for “query”") and the messages are announced: `role="alert"` for the error toast, `role="status"` for the others | Screen readers get told that results arrived |
| D15 | Messages have three styles: red (`--danger`) for errors, yellow (`--warn`) for "AI unavailable", accent-colored information (`--accent`) for the weak-match message and the empty state | They mean different things |
| D16 | The error toast has a "Try again" button, and the empty-state toast suggests a shorter query | Gives every dead end a next step |
| D17 | Placeholder books on the shelf while searching (replaced by the progress stage, D22), and the hero shrinks after the first search | No layout jump, and results start higher on small screens |
| D18 | The ledge sits directly under the covers, with the title and author below it | Covers share one height, so every row gets a continuous ledge with no gap under shorter cards |
| D19 | The search box and the example chips are disabled while a search runs, like the Search button | Input cannot change or start a second search mid-run. A disabled chip is dimmed (opacity 0.6, `progress` cursor, no hover highlight), like the Search button |
| D20 | Results layout: the top match is shown large with its reason always visible, and the other matches are compact cards with a badge and a native collapsed "Why this book?" (`<details>`). Replaces the shelf, its hover popover and Escape handling | Chosen from three prototyped layouts (shelf, ranked list, featured plus rest). The first result is the answer most of the time, so its reason is shown without hover or tap, and the reason works the same on touch and keyboard |
| D21 | Messages are toasts: the error ("Search failed" with Try again), the AI-unavailable notice ("Basic search mode"), the weak-match message ("Closest results") and the empty state (the title "No matching books found." plus the tip). They are stacked in a fixed container, at the bottom right on desktop and across the bottom on phones (600px and below), each with a × button. A dismissed toast returns after the next search, except "Basic search mode", which stays dismissed for the rest of the page session. While a toast shows, the page gets 150px of bottom padding, or 260px when two toasts stack (`.page:has(.toast)`). Replaces the full-width banners and the inline empty-state text | Chosen from four prototyped styles (the existing banners, a toast, a balloon under the search box with a popover pill, and a modal with a header chip). A toast does not move the layout, can be dismissed and keeps Try again next to the message. The first version put the toast at the top on phones and covered the logo, the heading and the search box (F2 in `docs/ui-tests/README.md`), so it moved to the bottom. The prototype was not kept on a throwaway branch |
| D22 | Search progress is one illustrated stage in place of the progress panel and the results skeleton: an animated SVG scene per step (a magnifier over the query, a library with radar rings, clues linked to book spines, a quill writing lines), the step title and a detail line, and four step chips (pending, active, done). Each step has its own time (understand 10 s, search 2 s, match 1 s, explain 30 s) and after the last one the stage says the search is taking longer than usual. The scenes use the theme CSS variables and are static under `prefers-reduced-motion` | Chosen from four prototyped variants: the existing panel, a stage, a floating dock pinned to the screen corner and a shelf where one book is added per step. The stage tells the user what is happening most clearly and works the same at every width. The variants are kept on the branch `prototype/search-progress-variants` (switch with `?variant=A` to `D`) |
| D23 | When a search finds no books, a picture of a mostly empty bookshelf (`EmptyShelf`, an inline SVG with `role="img"` and the label "An empty bookshelf") fills the area under the search box. The message stays a toast (D21). The picture stays after the toast is dismissed | The area under the search box was blank, and after the toast was dismissed the page showed nothing about the search (F4 in `docs/ui-tests/README.md`) |

## 2. Proposals

Three static HTML mockups with the same sample data (the "tolkien hobbit illustrated deluxe 1937" results, real Open Library covers) and a light/dark toggle. Open the files directly in a browser. They use no app code.

| | P1 Reading Room | P2 Bookshelf | P3 Modern Catalog |
|---|---|---|---|
| File | `1-reading-room.html` | `2-bookshelf.html` | `3-modern-catalog.html` |
| Theme fit | Strongest | Strong | Modern, utilitarian |
| Recognition by cover | Medium | Best | Medium |
| Information density | Medium | Low | Best |
| API change | None (optional edition count) | `matchTier` | `matchTier`, author match kind, year match, `editionCount` |
| Estimated effort | About 1 day | About 1 to 1.5 days | About 2 to 3 days |
| Status | Not implemented | Implemented | Not implemented |

### P1: Reading Room

- Catalog-card look. Warm paper palette in light mode, "walnut and lamp" in dark mode, serif type, brass and oxblood accents, mono call-number labels.
- The search sits in a "CATALOGUE" drawer. Each result is a ruled index card with a cover, small-caps author, a call-number style label and a "Librarian's note" holding the explanation. The AI-unavailable notice is a rubber stamp.
- Risk: the ruled-line texture needs a contrast check, and it is the most decorative option.

### P2: Bookshelf (chosen)

- A clean hero search and a grid of large covers standing on wooden ledges, with badges (Best match, Exact title, Author match, Close title). Title and author sit under each cover.
- The explanation appears on hover or keyboard focus. A generated blue cover with the title replaces a missing cover.
- Risk: hover does not work on touch, so a tap alternative was required (see D10).

### P3: Modern Catalog

- OPAC-style, three panes: a filter rail (match quality, author, example chips), a dense list with thumbnails, year, editions and reason chips ("Exact title", "Primary author", "Year 1937 matches"), and a sticky detail pane with "Why this book".
- Indigo and zinc palette. The filters only add value with more than 5 results, which the app does not return.

## 3. Implementation

### 3.1 API change

`BookResult` gained two fields (`Models.cs`, set in `SearchService.ToResult`, which now takes the `MatchedBook`):

- `matchTier`: integer 1 (strongest) to 5.
- `exactTitle`: true when the title matched exactly rather than nearly.

### 3.2 Files

| File | Role |
|------|------|
| `web/src/components/App/App.tsx` | Page: header (logo, theme toggle), hero (heading, search, example chips), toasts (error with Try again, AI notice, weak-match message, empty state), progress stage, empty bookshelf, results heading, featured results |
| `web/src/components/Toast/Toast.tsx`, `Toast.css`, `Toast.test.tsx` | The toast component (kinds `error`, `notice`, `info`), its styles (fixed container, phone layout, reduced motion) and its tests |
| `web/src/components/SearchProgress/SearchProgress.tsx`, `Scenes.tsx`, `SearchProgress.test.tsx` | The progress stage, the four animated scenes and the stage tests (replaces `ResultsSkeleton`, which was deleted) |
| `web/src/components/EmptyShelf/EmptyShelf.tsx` | The empty bookshelf picture shown when a search finds no books |
| `web/src/components/App/App.css` | All styles except the toast's: theme tokens (including `--danger` and `--warn`), hero (and its compact state), featured results, badges, mobile rules, progress stage and its scenes, empty shelf, theme toggle, page padding while a toast shows |
| `web/src/components/FeaturedResults/FeaturedResults.tsx` | The top match and the other matches (replaces `ResultCard`) |
| `web/src/lib/badge.ts` | `badgeFor(book, rank)` returns the badge kind and label |
| `web/src/lib/types.ts` | `BookResult` and `SearchResponse` including `matchTier` and `exactTitle` |
| `web/index.html` | Viewport meta with `viewport-fit=cover`, inline theme script |
| `web/src/components/App/App.test.tsx` | Updated for the layout, plus a badge test |

### 3.3 Behavior

- **Badges** (`lib/badge.ts`): "Best match" for the first result when its title is exact. "Exact title" for other exact titles. "Author match" for tier 4. "Close title" for everything else.
- **Cover:** links to the Open Library page (`aria-label` is the title plus "view on Open Library"). A missing cover renders a generated cover whose text comes from a `data-title` attribute through CSS `::before`, so the title is not duplicated in the DOM.
- **Featured match:** the first result is an `article` with a large cover, badge, `h3` title, author and year, its explanation (always visible) and a "View on Open Library" link. The cover also links to Open Library.
- **Other matches:** under an `h3` "Other possible matches", a 2-column list of cards (cover, badge, `h4` title, author and year). The reason is a native `<details>` "Why this book?", collapsed by default, so it works the same with mouse, touch and keyboard.
- **Results heading:** an `h2` ("N books for “query”") that receives focus when results arrive. The featured title is `h3`.
- **Toasts** (D21): `Toast` takes `kind` (`error`, `notice`, `info`), a title, a message, `onDismiss` and an optional action. The error toast is `role="alert"`, the others `role="status"`. `App` keeps a list of dismissed toasts and clears it at the start of every search. The weak-match toast shows only next to results.
- **Recovery:** the error toast shows "Try again", which repeats the last search. The empty-state toast has the server message as its title and the tip "Try just the author's last name, or fewer words." as its text.
- **While searching:** the progress stage (D22) replaces the results area. It is an `h3` title with a `role="status"` wrapper, so the step is announced. The scene animations stop under `prefers-reduced-motion`.
- **No results:** the empty bookshelf (D23) shows under the search box, with the empty-state toast. It is not rendered for results or for an error.
- **Compact hero:** after the first search the hero gets the `compact` class: smaller heading, no lead text, and the example chips in one scrolling row with long chips truncated.
- **Theme tokens:** `--bg`, `--surface`, `--ink`, `--muted`, `--line`, `--accent`, `--on-accent`, `--wood1`, `--wood2`, `--best`, `--exact`, `--close`, `--glow`. Dark values apply for `data-theme="dark"` and for System mode when the OS prefers dark. In dark mode the accent is light blue, so `--on-accent` is dark for readable button text.
- **Hero:** heading "What was that book?", a pill-shaped search input with the Search button inside, example chips below. The AI-unavailable notice is a yellow toast.

### 3.4 Mobile

| Rule | Applies at |
|------|-----------|
| Other matches in 1 column (the featured match narrows to a 110px cover) | 760px and below |
| Page padding respects device safe areas (`viewport-fit=cover`) | 760px and below |
| Search button at least 44px tall, chips and the "Why this book?" button at least 36px | 760px and below |
| Example chips scroll sideways in one row | 760px and below |
| Header wraps and the theme toggle is tighter | 420px and below |

## 4. Verification

Automated: 32 frontend tests passed at that point (45 after the shareable link, saved examples and weak-match notice) (Vitest and Testing Library), including a test that checks the three badge labels and that the toggle flips `aria-expanded`. 202 backend tests passed at that point (287 now, after the health endpoints in `2344f97` and later work).

By hand in headed Chrome:

- **Desktop, dark mode:** covers load, badges show, the hover popover shows the explanation.
- **About 250px window:** after the header fix there was no horizontal scroll, and the inline explanation opened.
- **390px (a 390x800 iframe on the page):** 2 columns, no horizontal scroll, chips scroll, the inline explanation opens, light mode renders correctly.

After the accessibility and flow pass (D13 to D18): 53 frontend tests pass. By hand in Chrome: desktop dark and light (ledge under the covers, compact hero, focused results heading, skeleton while searching) and a 375px iframe in light mode (2 columns, no horizontal scroll). Escape and Try again are covered by tests only, not tried by hand.

Featured layout (D20): 58 frontend tests pass. Full report with screenshots in `docs/ui-tests/README.md`. By hand in headed Chrome (the window resize did not change the viewport, so widths were tested in same-origin iframes): 1440px full window in dark mode, and 1024, 768, 390 and 320px in dark and light, with the "Why this book?" disclosures opened. No horizontal overflow at any width. Other matches are 2 columns at 1024 and 768px and 1 column at 390 and 320px. Not tried in that run: real touch, the skeleton, a single-result search, the empty and error states (all tried in the toast runs below), widths below 320px. On desktop there is a gap between the explanation and the "View on Open Library" link, because the link sits at the bottom of the card.

Toast messages (D21): 67 frontend tests pass. Three by-hand runs in headed Chrome, with stubbed responses for the error, weak-match and empty cases (report and screenshots in `docs/ui-tests/README.md`). They covered every toast kind at 1024, 768, 390 and 320px in dark and light, dismissing and Try again, the skeleton, a single-result search, and 24 combinations of the AI notice and the info toast, with nothing covered at the end of the page. The first run found the toast covering the logo, the heading and the search box on phones (F2), which was fixed by moving it to the bottom. Not tried: real touch, a screen reader, a real failed search, a browser without `:has()`.

Progress stage and empty bookshelf (D22, D23): 69 frontend tests pass. The by-hand run was repeated in headed Chrome (report and screenshots in `docs/ui-tests/README.md`, all 20 scenarios pass). The stage was checked at 1024, 768, 390 and 320px in dark and light, all four scenes in order, the slow state after 10 seconds, motion (sampled values over 10.5 seconds and stills) and reduced motion (forced on in a frame). The bookshelf was checked at 1024, 390 and 320px in dark and light, with a real empty search. Not tried: real touch, a screen reader, watching or recording the animation for smoothness.

Not verified: widths between 360 and 430px on a real device, the 1-column layout below 340px, real touch behavior (the iframe does not trigger `hover: none`), landscape orientation, and screen reader behavior of the toggle and the results heading.

## 5. Known issues and open items

- Enter does nothing while a search runs, because the search box and the Search button are disabled.
- Proposal 3's filters and P1's theme are not implemented. A hybrid (the featured layout with P3's reason chips) would reuse `matchTier` and `exactTitle`, but needs the author match kind and year match in the API.
- Authors and titles are shown as Open Library stores them, so they can be in another script.
- Open Library stores some titles with the Unicode replacement character in place of accented letters (for example work `OL30135961W`, "Qu\ufffdmica Org\ufffdnica"). The API passes them through unchanged, so they show as `?` boxes. The original letters cannot be recovered, and no filter was added.
- The page padding that keeps the last result clear of the toasts uses `:has()`. Without it a toast can sit over the last result.
