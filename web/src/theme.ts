export type ThemeMode = "system" | "light" | "dark";

const KEY = "ftb-theme";

export function isThemeMode(value: unknown): value is ThemeMode {
  return value === "system" || value === "light" || value === "dark";
}

/** Storage can be blocked (private mode, policies), so every access is guarded. */
export function readStoredMode(): ThemeMode {
  try {
    const value = localStorage.getItem(KEY);
    return isThemeMode(value) ? value : "system";
  } catch {
    return "system";
  }
}

export function storeMode(mode: ThemeMode): void {
  try {
    localStorage.setItem(KEY, mode);
  } catch {
    // Not persisted; the choice still applies for this page view.
  }
}

/** "system" removes the attribute so the OS preference (via color-scheme) decides, live. */
export function applyMode(mode: ThemeMode): void {
  const root = document.documentElement;
  if (mode === "system") root.removeAttribute("data-theme");
  else root.setAttribute("data-theme", mode);
}
