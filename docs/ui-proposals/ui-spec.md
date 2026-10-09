# Find That Book: UI Spec

Scope: every UI decision, the three design proposals, and what was implemented. The backend design is in `docs/superpowers/specs/2026-10-08-find-that-book-design.md`.

Status: Bookshelf (proposal 2) and the mobile layout are implemented and merged into `master` (commits `fc9615b`, `2eb23df`, `5adc586`, docs in `99e3f85`). The accessibility and flow pass (D13 to D18) is in `659cc63`, also merged into `master`. The results layout was then changed from the shelf to a featured top match plus compact cards (D20), which supersedes the shelf-specific parts of D9, D10, D13, D17 and D18. Sections 3.2 to 3.4 describe the featured layout.

## 1. Decisions

| # | Decision | Why |
|---|----------|-----|
| D1 | React + Vite + TypeScript, served by the API in one container | One deployment, no CORS |
| D2 | Source split into `web/src/components/<Name>/` and `web/src/lib/` | Each component has its own folder and tests |
| D3 | Theme: System (default), Light, Dark through `data-theme` on `<html>`, stored under `ftb-theme`, applied by an inline script before first paint | No flash of the wrong theme, follows the OS |
| D4 | A timed progress panel while searching (the API reports no progress) | Searches take several seconds |
| D5 | Example chips promote successful searches and are saved in `localStorage` | Shows what works and keeps it between visits |
| D6 | A late response from an older search is ignored | Results never show for the wrong query |
| D7 | Design direction: Bookshelf (proposal 2) | The task is "is this the book I remember?", and large covers answer that fastest |
| D8 | The API returns `matchTier` and `exactTitle` per result | Badges need the match tier, and parsing the explanation text would be fragile |
| D9 | The explanation is hidden by default and shown on hover, focus or tap | Keeps the shelf scannable |
| D10 | On narrow screens and touch devices the explanation opens inline, not as a popover | A popover overlapped neighbouring cards and was too narrow in 2 columns |
| D11 | Colors are CSS variables defined per theme in `App.css` | One place to change the palette, used by the shelf, progress panel and toggle |
| D12 | The three proposals stay in the repo as static mockups | They document the alternatives |
| D13 | Escape closes the explanation and keeps the hover popover dismissed until the pointer or focus leaves the card | WCAG 1.4.13: hover content must be dismissable and hoverable |
| D14 | After a search with results, focus moves to a heading ("N books for “query”") and the notices and empty state are `role="status"` | Screen readers get told that results arrived |
| D15 | Notices have two styles: yellow for "AI unavailable", accent-colored information for the weak-match message | They mean different things |
| D16 | The error banner has a "Try again" button, and the empty state suggests a shorter query | Gives every dead end a next step |
| D17 | Placeholder books on the shelf while searching, and the hero shrinks after the first search | No layout jump, and results start higher on small screens |
| D18 | The ledge sits directly under the covers, with the title and author below it | Covers share one height, so every row gets a continuous ledge with no gap under shorter cards |
| D19 | The search box and the example chips are disabled while a search runs, like the Search button | Input cannot change or start a second search mid-run. A disabled chip is dimmed (opacity 0.6, `progress` cursor, no hover highlight), like the Search button |
| D20 | Results layout: the top match is shown large with its reason always visible, and the other matches are compact cards with a badge and a native collapsed "Why this book?" (`<details>`). Replaces the shelf, its hover popover and Escape handling | Chosen from three prototyped layouts (shelf, ranked list, featured plus rest). The first result is the answer most of the time, so its reason is shown without hover or tap, and the reason works the same on touch and keyboard |

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
| `web/src/components/App/App.tsx` | Page: header (logo, theme toggle), hero (heading, search, example chips), banners (with Try again), progress panel, results skeleton, results heading, featured results |
| `web/src/components/ResultsSkeleton/ResultsSkeleton.tsx` | Placeholder shaped like the featured match and two other matches, shown while a search runs |
| `web/src/components/App/App.css` | All styles: theme tokens, hero (and its compact state), featured results, badges, mobile rules, skeleton, progress panel, theme toggle |
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
- **Notices:** "AI unavailable" is yellow, the weak-match message uses the `info` style. Both and the empty state are `role="status"`.
- **Recovery:** the error banner shows "Try again", which repeats the last search. The empty state adds the tip "Try just the author's last name, or fewer words."
- **While searching:** the progress panel is followed by a skeleton shaped like the featured layout. Their shimmer stops under `prefers-reduced-motion`.
- **Compact hero:** after the first search the hero gets the `compact` class: smaller heading, no lead text, and the example chips in one scrolling row with long chips truncated.
- **Theme tokens:** `--bg`, `--surface`, `--ink`, `--muted`, `--line`, `--accent`, `--on-accent`, `--wood1`, `--wood2`, `--best`, `--exact`, `--close`, `--glow`. Dark values apply for `data-theme="dark"` and for System mode when the OS prefers dark. In dark mode the accent is light blue, so `--on-accent` is dark for readable button text.
- **Hero:** heading "What was that book?", a pill-shaped search input with the Search button inside, example chips below. The AI-unavailable notice is a centered yellow banner.

### 3.4 Mobile

| Rule | Applies at |
|------|-----------|
| Other matches in 1 column (the featured match narrows to a 110px cover) | 760px and below |
| Page padding respects device safe areas (`viewport-fit=cover`) | 760px and below |
| Search button at least 44px tall, chips and the "Why this book?" button at least 36px | 760px and below |
| Example chips scroll sideways in one row | 760px and below |
| Header wraps and the theme toggle is tighter | 420px and below |

## 4. Verification

Automated: 32 frontend tests passed at that point (45 after the shareable link, saved examples and weak-match notice) (Vitest and Testing Library), including a test that checks the three badge labels and that the toggle flips `aria-expanded`. 202 backend tests passed at that point (264 after the health endpoints were added in `2344f97`).

By hand in headed Chrome:

- **Desktop, dark mode:** covers load, badges show, the hover popover shows the explanation.
- **About 250px window:** after the header fix there was no horizontal scroll, and the inline explanation opened.
- **390px (a 390x800 iframe on the page):** 2 columns, no horizontal scroll, chips scroll, the inline explanation opens, light mode renders correctly.

After the accessibility and flow pass (D13 to D18): 53 frontend tests pass. By hand in Chrome: desktop dark and light (ledge under the covers, compact hero, focused results heading, skeleton while searching) and a 375px iframe in light mode (2 columns, no horizontal scroll). Escape and Try again are covered by tests only, not tried by hand.

Featured layout (D20): 58 frontend tests pass. Full report with screenshots in `docs/ui-tests/README.md`. By hand in headed Chrome (the window resize did not change the viewport, so widths were tested in same-origin iframes): 1440px full window in dark mode, and 1024, 768, 390 and 320px in dark and light, with the "Why this book?" disclosures opened. No horizontal overflow at any width. Other matches are 2 columns at 1024 and 768px and 1 column at 390 and 320px. Not tried: real touch, the skeleton, a single-result search, the empty and error states, widths below 320px. On desktop there is a gap between the explanation and the "View on Open Library" link, because the link sits at the bottom of the card.

Not verified: widths between 360 and 430px on a real device, the 1-column layout below 340px, the compact hero and skeleton in dark mode at 390px, real touch behavior (the iframe does not trigger `hover: none`), landscape orientation, and screen reader behavior of the toggle and the results heading.

## 5. Known issues and open items

- Enter does nothing while a search runs, because the search box and the Search button are disabled.
- Proposal 3's filters and P1's theme are not implemented. A hybrid (the featured layout with P3's reason chips) would reuse `matchTier` and `exactTitle`, but needs the author match kind and year match in the API.
- Authors and titles are shown as Open Library stores them, so they can be in another script.
