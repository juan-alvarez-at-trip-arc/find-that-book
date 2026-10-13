/* Apply a saved theme before first paint to avoid a flash; keep in sync with src/lib/theme.ts. */
try {
  var m = localStorage.getItem("ftb-theme");
  if (m === "light" || m === "dark") document.documentElement.setAttribute("data-theme", m);
} catch {}
