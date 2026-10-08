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
