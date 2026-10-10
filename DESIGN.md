---
name: Find That Book
description: A warm paper desk where one half-remembered book is pulled forward, with the runners-up beside it.
colors:
  warm-paper: "#f4efe7"
  card-paper: "#fffdf9"
  ink: "#221f1a"
  muted-ink: "#5e5a52"
  paper-line: "#e2d9ca"
  library-blue: "#2a5fd1"
  on-library-blue: "#ffffff"
  spine-green: "#1f7a56"
  spine-blue: "#2f6fed"
  spine-amber: "#9a6a08"
  alert-red: "#c0392b"
  warn-amber: "#9a6a08"
  ledge-light: "#b98b5e"
  ledge-dark: "#8d6340"
  cover-slate: "#4b5d83"
  cover-navy: "#2d3a57"
typography:
  display:
    fontFamily: "\"Iowan Old Style\", \"Palatino Linotype\", Palatino, Georgia, serif"
    fontSize: "clamp(1.9rem, 4vw, 2.7rem)"
    fontWeight: 700
    lineHeight: 1.2
    letterSpacing: "-0.03em"
  headline:
    fontFamily: "\"Iowan Old Style\", \"Palatino Linotype\", Palatino, Georgia, serif"
    fontSize: "1.6rem"
    fontWeight: 700
    lineHeight: 1.2
    letterSpacing: "-0.02em"
  title:
    fontFamily: "\"Iowan Old Style\", \"Palatino Linotype\", Palatino, Georgia, serif"
    fontSize: "0.95rem"
    fontWeight: 700
    lineHeight: 1.25
  body:
    fontFamily: "system-ui, \"Segoe UI\", sans-serif"
    fontSize: "1rem"
    fontWeight: 400
    lineHeight: 1.5
  label:
    fontFamily: "system-ui, \"Segoe UI\", sans-serif"
    fontSize: "0.72rem"
    fontWeight: 700
    letterSpacing: "0.04em"
rounded:
  cover: "3px 6px 6px 3px"
  card-sm: "12px"
  card-lg: "16px"
  pill: "999px"
spacing:
  xs: "8px"
  sm: "12px"
  md: "20px"
  lg: "28px"
components:
  search-bar:
    backgroundColor: "{colors.card-paper}"
    textColor: "{colors.ink}"
    rounded: "{rounded.pill}"
    padding: "6px"
  button-primary:
    backgroundColor: "{colors.library-blue}"
    textColor: "{colors.on-library-blue}"
    rounded: "{rounded.pill}"
    padding: "0 26px"
  chip:
    backgroundColor: "{colors.card-paper}"
    textColor: "{colors.muted-ink}"
    rounded: "{rounded.pill}"
    padding: "4px 12px"
  card-featured:
    backgroundColor: "{colors.card-paper}"
    textColor: "{colors.ink}"
    rounded: "{rounded.card-lg}"
    padding: "24px"
  card-mini:
    backgroundColor: "{colors.card-paper}"
    textColor: "{colors.ink}"
    rounded: "{rounded.card-sm}"
    padding: "12px"
  badge-best:
    backgroundColor: "{colors.spine-green}"
    textColor: "{colors.on-library-blue}"
    rounded: "{rounded.pill}"
    padding: "3px 10px"
  badge-exact:
    backgroundColor: "{colors.spine-blue}"
    textColor: "{colors.on-library-blue}"
    rounded: "{rounded.pill}"
    padding: "3px 10px"
  badge-close:
    backgroundColor: "{colors.spine-amber}"
    textColor: "{colors.on-library-blue}"
    rounded: "{rounded.pill}"
    padding: "3px 10px"
  toast:
    backgroundColor: "{colors.card-paper}"
    textColor: "{colors.ink}"
    rounded: "{rounded.card-sm}"
    padding: "12px 10px 12px 14px"
---

# Design System: Find That Book

## Overview

**Creative North Star: "The Library Desk"**

A worktable where a reader checks a half-remembered book. The page is warm paper, the question sits at the top in a serif, and the answer arrives as one large card with its cover standing on a wooden ledge. Runners-up sit beside it as smaller cards. The system is calm and bookish, built for one task: deciding whether this is the book you remember.

The look comes from objects on a desk: paper, ink, wood and cloth covers. A single blue accent marks actions and the current state. Three spine colors (green, blue, amber) carry match quality and nothing else. Color and depth stay quiet so the cover and the one-sentence reason are what the eye lands on.

**Key Characteristics:**
- Warm paper page with a faint blue glow at the top edge.
- Serif for the question and for book titles, system sans for everything that operates the tool.
- One blue accent for actions, focus and progress.
- Covers are tactile: spine shadow, thin edge and a wooden ledge beneath.
- Pill-shaped controls, softly rounded cards, soft lift shadows.
- Light, dark and system themes share one token set. Colors come only from CSS variables in `App.css`.

## Colors

A warm neutral ground with one cool accent. Wood tones appear only as ledges and in the empty shelf.

### Primary
- **Library Blue** (#2a5fd1): Search button, links, focus rings, progress strokes, the active step. In dark mode it becomes a lighter blue (#7aa7ff) with dark text (#0b1220) on it.

### Secondary
- **Spine Green** (#1f7a56): "Best match" badge and the confirmed-step check.
- **Spine Blue** (#2f6fed): "Exact title" badge.
- **Spine Amber** (#9a6a08): "Close title" and "Author match" badges, and the "Basic search mode" notice.

### Tertiary
- **Alert Red** (#c0392b): The error toast and nothing else.

### Neutral
- **Warm Paper** (#f4efe7): Page background.
- **Card Paper** (#fffdf9): Cards, the search bar, chips, toasts.
- **Ink** (#221f1a): Body and title text.
- **Muted Ink** (#5e5a52): Bylines, chip text, secondary copy.
- **Paper Line** (#e2d9ca): Borders and dividers.
- **Ledge Light** (#b98b5e) and **Ledge Dark** (#8d6340): The wood gradient under covers and the empty shelf.
- **Cover Slate** (#4b5d83) and **Cover Navy** (#2d3a57): The generated cover when a book has none.

Dark theme: warm near-black ground (#16140f), card (#201d17), ink (#f1ede4), muted (#aca597), line (#38322a), lighter spine and cover colors so badges and placeholder covers keep contrast.

### Named Rules
**The Spine Colors Rule.** Green, blue and amber mean match quality and only that. They never decorate.

**The One Accent Rule.** Library Blue is the only color that asks for an action. Wood tones are scenery, never interactive.

**The Variables Only Rule.** No color literal appears in a component, apart from the translucent black used in shadows and thin strokes. Every other color is a variable in `App.css`.

## Typography

**Display Font:** Iowan Old Style (with Palatino Linotype, Palatino, Georgia, serif)
**Body Font:** system-ui (with Segoe UI, sans-serif)

**Character:** A bookish serif for the question and for titles, a plain system sans for controls and notes. The serif says "book", the sans says "tool".

### Hierarchy
- **Display** (700, clamp(1.9rem, 4vw, 2.7rem), 1.2): the page heading "What was that book?". Shrinks to clamp(1.3rem, 3vw, 1.6rem) after the first search.
- **Headline** (700, 1.6rem, 1.2): the featured book's title. 1.15rem on phones.
- **Title** (700, 0.95rem, 1.25): titles in the smaller cards.
- **Body** (400, 1rem, 1.5): the reason sentence on the featured card, 0.85rem in smaller cards.
- **Label** (700, 0.72rem, 0.04em, uppercase): badges. "Other possible matches" uses 0.85rem with 0.05em tracking.

### Named Rules
**The Serif For Books Rule.** Only the page question and book titles use the serif. Controls, bylines, toasts and badges stay sans.

## Layout

A single centered column, 1040px at most, with 20px side padding (14px on phones, respecting device safe areas). The hero is centered: heading, lead line, search bar (680px at most), example chips. After the first search the hero compacts: smaller heading, no lead line, and chips in one sideways-scrolling row.

Results sit in an 860px column. The featured card is a two-column grid: a 200px cover, a 28px gap, then text. The other matches are a two-column grid with 12px gaps, one column at 760px and below, where the featured cover narrows to 110px. Messages are toasts in a fixed container at the bottom right (across the bottom at 600px and below). While a toast shows, the page reserves extra bottom padding so nothing is covered.

Touch targets: every control is at least 24px tall at any width. Below 760px the Search button is at least 44px tall and the chips and "Why this book?" at least 36px.

## Elevation & Depth

Hybrid. Surfaces sit on soft diffuse shadows, and covers add a tactile layer of their own: an inset spine shadow, a thin edge and a wooden ledge beneath.

### Shadow Vocabulary
- **Card lift** (`box-shadow: 0 8px 30px #0001`): search bar, featured card, progress stage.
- **Toast lift** (`box-shadow: 0 10px 30px #0003`): toasts, since they float above content.
- **Cover** (`box-shadow: -3px 0 0 #0002 inset, 3px 6px 12px #0004`): every cover, with a 1px outline from `--cover-edge`.
- **Ledge** (`box-shadow: 0 5px 8px #0003`): the wooden bar under each cover (7px high on the featured card, 5px on the small ones).

### Named Rules
**The Shelf Rule.** A cover always stands on a ledge. The ledge sits directly beneath the cover and is the same width plus a small overhang.

## Shapes

Pills for controls (999px): search bar, button, chips, badges, theme toggle. Cards are softly rounded: 16px for the featured card and progress stage, 12px for smaller cards and toasts. Covers keep a book-like asymmetric corner (3px on the spine side, 6px on the page side). Borders are always 1px Paper Line, except a toast, whose border is tinted by its kind.

## Components

### Buttons
- **Shape:** pill (999px).
- **Primary:** Library Blue background, white text (dark text in dark mode), 600 weight, 0 26px padding, inside the search bar.
- **Hover / Focus:** 3px Library Blue focus ring with 2px offset. Disabled is 60% opacity with a progress cursor.
- **Toast action:** outlined in the toast's kind color, transparent fill, tinted on hover.

### Chips
- **Style:** Card Paper fill, Muted Ink text, 1px Paper Line border, 0.85rem.
- **State:** hover turns the text to Ink and the border to Library Blue. Disabled dims to 60%. On phones and after the first search they scroll sideways in one row.

### Cards / Containers
- **Corner Style:** 16px featured, 12px smaller.
- **Background:** Card Paper with a 1px Paper Line border.
- **Shadow Strategy:** Card lift for the featured card. Smaller cards stay flat.
- **Internal Padding:** 24px featured, 12px smaller (14px on phones).

### Inputs / Fields
- **Style:** one search field inside a pill bar, transparent, 1.05rem, caret in Library Blue.
- **Focus:** the whole bar shows a 3px Library Blue ring at 40% opacity. The placeholder uses Muted Ink.
- **Error / Disabled:** the field is disabled while a search runs. Errors appear as toasts, never inline.

### Navigation
A header row with the wordmark "FindThatBook" (the middle word in Library Blue), which links to `/` and reloads the page with no query and an empty search box (a visible focus ring for keyboard users) and a three-option theme toggle (System, Light, Dark) as a pill group. Pressed state is Library Blue with its on-color. The header wraps below 420px.

### Featured Card (signature)
The answer. Cover on its ledge at left, a badge, the serif title, the byline, an optional bold "Catalogued as" line with the stored work title (the headline is the matched edition title when there is one), the always-visible reason sentence and a "View on Open Library" link pinned to the bottom. The cover link is hidden from assistive tech and the tab order, so that text link is the one link, and it says it opens a new tab.

### Toast (signature)
Messages float at the bottom right: an icon, a bold title, a muted message and a close button. Kinds: error (Alert Red), notice (Warn Amber), info (Library Blue). Errors carry a Try again button. The icon mark (the "!" or "i") is drawn in the on-accent color so it reads on the kind color in both themes.

### Progress Stage (signature)
While searching, one illustrated scene per step (a magnifier, a library with radar rings, clues linked to spines, a quill) with a step title, a detail line and four step chips. Scenes use the theme variables and stand still under reduced motion.

## Do's and Don'ts

### Do:
- **Do** color everything through the CSS variables in `App.css`, with a value for light and for dark.
- **Do** keep the reason sentence visible on the featured card and collapsed behind "Why this book?" on the others.
- **Do** stand every cover on a wooden ledge and keep the cover's 2:3 ratio.
- **Do** use the serif only for the page question and book titles.
- **Do** show messages as toasts and keep the EmptyShelf picture text-free.
- **Do** check light, dark and a 390px width after any change.

### Don't:
- **Don't** use the spine colors (green, blue, amber) for decoration.
- **Don't** add inline banners. Messages are toasts.
- **Don't** use a colored left border on cards, alerts or toasts.
- **Don't** let a badge claim more than the match tier supports. The badge reports the deterministic ranking and never changes it.
- **Don't** add a hover-only interaction. Everything works with touch and keyboard.
