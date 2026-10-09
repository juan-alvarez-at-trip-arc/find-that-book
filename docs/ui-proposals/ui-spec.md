# Find That Book: UI Spec

Scope: every UI decision, the three design proposals, and what was implemented. The backend design is in `docs/superpowers/specs/2026-10-08-find-that-book-design.md`.

Status: Bookshelf (proposal 2) and the mobile layout are implemented and merged into `master` (commits `fc9615b`, `2eb23df`, `5adc586`, docs in `99e3f85`).

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

- A clean hero search and a grid of large covers standing on wooden ledges, with badges (Best match, Exact title, Close title). Title and author sit under each cover.
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
| `web/src/components/App/App.tsx` | Page: header (logo, theme toggle), hero (heading, search, example chips), banners, progress panel, "N books" count, results list |
| `web/src/components/App/App.css` | All styles: theme tokens, hero, shelf, badges, popover, mobile rules, progress panel, theme toggle |
| `web/src/components/ResultCard/ResultCard.tsx` | One book: cover linking to Open Library, badge, title, author and year, "Why this book?" toggle, explanation |
| `web/src/lib/badge.ts` | `badgeFor(book, rank)` returns the badge kind and label |
| `web/src/lib/types.ts` | `BookResult` and `SearchResponse` including `matchTier` and `exactTitle` |
| `web/index.html` | Viewport meta with `viewport-fit=cover`, inline theme script |
| `web/src/components/App/App.test.tsx` | Updated for the layout, plus a badge and toggle test |

### 3.3 Behavior

- **Badges** (`lib/badge.ts`): "Best match" for the first result when its title is exact. "Exact title" for other exact titles. "Author match" for tier 4. "Close title" for everything else.
- **Cover:** links to the Open Library page (`aria-label` is the title plus "view on Open Library"). A missing cover renders a generated cover whose text comes from a `data-title` attribute through CSS `::before`, so the title is not duplicated in the DOM.
- **Explanation:** always in the DOM, hidden with `visibility` and `opacity`. Wide screens with a pointer show it on hover or keyboard focus (`:focus-within`). The "Why this book?" button toggles `data-open` and `aria-expanded` and is the only way to open it on touch.
- **Shelf:** a 4-column grid. Each book draws its ledge with a `::after` pseudo-element, so a ledge runs under every row. Cover hover lifts and tilts slightly, disabled under `prefers-reduced-motion`.
- **Theme tokens:** `--bg`, `--surface`, `--ink`, `--muted`, `--line`, `--accent`, `--on-accent`, `--wood1`, `--wood2`, `--best`, `--exact`, `--close`, `--glow`. Dark values apply for `data-theme="dark"` and for System mode when the OS prefers dark. In dark mode the accent is light blue, so `--on-accent` is dark for readable button text.
- **Hero:** heading "What was that book?", a pill-shaped search input with the Search button inside, example chips below. The AI-unavailable notice is a centered yellow banner.

### 3.4 Mobile

| Rule | Applies at |
|------|-----------|
| Grid is 2 columns | 760px and below |
| Grid is 1 column | 340px and below |
| Explanation opens inline under the card | 760px and below, and `(hover: none)` |
| Hover lift disabled | 760px and below, and `(hover: none)` |
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

Not verified: light mode of the desktop layout, widths between 360 and 430px on a real device, the 1-column layout below 340px, real touch behavior (the iframe does not trigger `hover: none`), landscape orientation, and screen reader behavior of the toggle.

## 5. Known issues and open items

- F1: the "Why this book?" link sits about 2px to the right of the title.
- F2: in a 2-column row the ledge sits under the taller card, leaving a gap under the shorter one.
- Enter does nothing while a search runs, because the Search button is disabled.
- Proposal 3's filters and P1's theme are not implemented. A hybrid (the shelf with P3's reason chips) would reuse `matchTier` and `exactTitle`, but needs the author match kind and year match in the API.
- Authors and titles are shown as Open Library stores them, so they can be in another script.
