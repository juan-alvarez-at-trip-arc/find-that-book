# Product

<!-- impeccable:product-schema 1 -->

## Platform

web

## Users

Casual readers who half-remember a book. They have fragments (part of a title, an author's name, a year, a misspelling) and want to confirm "is this the book I remember?" quickly, on desktop or phone.

## Product Purpose

Turns a messy text query into up to 5 ranked Open Library matches, each with a one-sentence explanation. Success is the reader recognizing their book from the top match or the cover, without needing to refine the query.

## Positioning

Tolerates noisy, partial, misspelled and non-English input, ranks with deterministic code (primary authors above contributors), and explains every match in one sentence that cannot contradict the order. AI only extracts the query and writes explanations. Searching Open Library directly does not do this.

## Operating Context

- .NET 10 Web API plus React/Vite/TypeScript UI, served from one container. Data comes from Open Library, which rate-limits, so searches are slow at times (several seconds) and the UI shows an illustrated progress stage.
- AI provider is Gemini or any OpenAI-compatible endpoint. It can be unavailable, in which case a heuristic parser and template explanations answer and the response reports `aiUsed: false`.

## Capabilities and Constraints

- Up to 5 results: a featured top match with its reason always visible, other matches as compact cards with badges (Best match, Exact title, Author match, Close title) and a collapsed "Why this book?".
- Messages (errors with Try again, AI fallback, weak match, empty state) are toasts, never inline banners. No results also shows an empty bookshelf picture with no text.
- Light, dark and system themes. Colors live only in CSS variables in `App.css`. Check light, dark and a 390px width after every UI change.
- Example chips promote successful searches and are saved in `localStorage`. Searches are shareable through `?q=` links.
- Ranking stays deterministic. No LLM re-ranking.

## Brand Commitments

Name: Find That Book. Heading copy "What was that book?". The Bookshelf direction (large covers, wooden ledge motif, generated blue cover for missing covers) was chosen over Reading Room and Modern Catalog (`docs/ui-proposals/ui-spec.md`, D7).

## Evidence on Hand

- Real Open Library data and covers. No testimonials, customer logos, usage numbers or hosted demo exist, so none may be invented.
- By-hand UI test results in `docs/ui-tests/README.md`.

## Product Principles

1. Recognition first: the reader should confirm the right book at a glance (cover, title, author, one reason).
2. Honest about uncertainty: say so when AI was not used or the match is weak.
3. Every dead end has a next step.
4. Work the same on touch, keyboard and screen reader, on any width.
5. Never overload Open Library: the UI must not encourage extra requests.

## Accessibility & Inclusion

Full accessibility is required: WCAG-level keyboard and screen reader support (already: focused results heading, announced toasts, Escape handling), reduced motion, sufficient contrast in light and dark. Also required: SEO, AEO (answer-engine readiness) and general web UX best practices (performance, semantic markup, metadata, responsive behavior). Exact standard target (for example WCAG 2.2 AA) was not specified and is undecided.
