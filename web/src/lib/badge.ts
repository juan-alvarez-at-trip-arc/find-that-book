import type { BookResult } from "./types";

export type BadgeKind = "best" | "exact" | "close";

/** Picks the shelf badge: the top exact match is the best match, other exact titles are exact, everything else is close. */
export function badgeFor(book: BookResult, rank: number): { kind: BadgeKind; label: string } {
  if (book.exactTitle && rank === 1) return { kind: "best", label: "Best match" };
  if (book.exactTitle) return { kind: "exact", label: "Exact title" };
  if (book.matchTier === 4) return { kind: "close", label: "Author match" };
  return { kind: "close", label: "Close title" };
}
