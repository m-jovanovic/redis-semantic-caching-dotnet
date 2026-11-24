(() => {
  "use strict";
  const storageKey = "semantic-cache-theme";
  const systemTheme = window.matchMedia("(prefers-color-scheme: dark)");
  const validTheme = (value) => value === "light" || value === "dark";
  let preference = null;
  try {
    const saved = localStorage.getItem(storageKey);
    if (validTheme(saved)) preference = saved;
  } catch {
    /* Storage can be unavailable in restricted browser contexts. */
  }

  function applyTheme() {
    const theme = preference ?? (systemTheme.matches ? "dark" : "light");
    document.documentElement.dataset.theme = theme;
    const toggle = document.getElementById("theme-toggle");
    if (toggle) {
      const label = `Switch to ${theme === "dark" ? "light" : "dark"} mode`;
      toggle.setAttribute("aria-label", label);
      toggle.title = label;
    }
  }

  // Apply before styles load so reloads do not flash the opposite theme.
  applyTheme();
  systemTheme.addEventListener("change", applyTheme);
  window.addEventListener("storage", (event) => {
    if (event.key !== storageKey && event.key !== null) return;
    preference = validTheme(event.newValue) ? event.newValue : null;
    applyTheme();
  });
  document.addEventListener("DOMContentLoaded", () => {
    applyTheme();
    document.getElementById("theme-toggle")?.addEventListener("click", () => {
      preference = document.documentElement.dataset.theme === "dark" ? "light" : "dark";
      try {
        localStorage.setItem(storageKey, preference);
      } catch {
        /* Keep the in-memory choice. */
      }
      applyTheme();
    });
  });
})();
