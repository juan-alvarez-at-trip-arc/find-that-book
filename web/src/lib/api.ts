import type { SearchResponse } from "./types";

export async function searchBooks(query: string): Promise<SearchResponse> {
  const response = await fetch("/api/search", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ query }),
  });

  if (!response.ok) {
    const problem = await response.json().catch(() => null);
    throw new Error(problem?.title ?? `Request failed (${response.status}).`);
  }
  return response.json();
}
