/**
 * Puts a successful query at the front of the examples. An example that is already listed (ignoring case and
 * surrounding spaces) is moved instead of duplicated, otherwise the last example is dropped. The length never changes.
 */
export function promoteExample(examples: readonly string[], query: string): string[] {
  const text = query.trim();
  if (!text) return [...examples];

  const rest = examples.filter((example) => example.trim().toLowerCase() !== text.toLowerCase());
  return [text, ...rest].slice(0, examples.length);
}

const KEY = "ftb-examples";

/** Returns the saved examples, or the defaults when nothing valid is saved or storage is blocked. */
export function readStoredExamples(defaults: readonly string[]): string[] {
  try {
    const parsed: unknown = JSON.parse(localStorage.getItem(KEY) ?? "null");
    const valid =
      Array.isArray(parsed) &&
      parsed.length === defaults.length &&
      parsed.every((item) => typeof item === "string" && item.trim().length > 0);
    return valid ? (parsed as string[]) : [...defaults];
  } catch {
    return [...defaults];
  }
}

/** Saves the examples. Storage can be blocked (private mode, policies), so a failure is ignored and the list lasts for this page view. */
export function storeExamples(examples: readonly string[]): void {
  try {
    localStorage.setItem(KEY, JSON.stringify(examples));
  } catch {
    // Not persisted.
  }
}
