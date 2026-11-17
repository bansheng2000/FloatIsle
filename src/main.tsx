import React from "react";
import ReactDOM from "react-dom/client";
import "./styles/tokens.css";
import "./styles/fonts.css";
import "./styles/base.css";
import { IslandApp } from "./windows/island/IslandApp";
import { TranslationWindow } from "./windows/translation/TranslationWindow";
import { SettingsWindow } from "./windows/settings/SettingsWindow";
import { I18nProvider } from "./i18n";
import { trackIslandTopEdge } from "./windows/island/topEdge";
import { installSameWindowStorageEvents } from "./lib/sameWindowStorageEvents";
import { bootTodoFile } from "./features/todos/fileSync";
import { loadSettings } from "./features/appearance/storage";
import { resolveColorScheme } from "./features/appearance/colorScheme";

const windowKind = new URLSearchParams(window.location.search).get("window");

/**
 * (E1) ⑤ The palette and texture live on the document root so the island, settings and translation
 * windows all follow one choice. The island stage repeats them for its own CSS layers; here the
 * root attributes drive the shared tokens. A "system" choice tracks the OS at draw time.
 */
function applyWindowTheme() {
  const settings = loadSettings();
  const lightQuery = window.matchMedia("(prefers-color-scheme: light)");
  const paint = (prefersLight: boolean) => {
    document.documentElement.dataset.colorScheme = resolveColorScheme(settings.colorScheme, prefersLight);
    document.documentElement.dataset.texture = settings.texture;
  };
  paint(lightQuery.matches);
  lightQuery.addEventListener("change", (event) => {
    if (settings.colorScheme === "system") paint(event.matches);
  });
}
applyWindowTheme();

if (windowKind === null) {
  trackIslandTopEdge();
  // Settings can be shown inside the island, which then shares this window with it.
  installSameWindowStorageEvents();
}

window.addEventListener(
  "pointerdown",
  () => {
    document.documentElement.dataset.inputModality = "pointer";
  },
  true,
);

window.addEventListener(
  "keydown",
  (event) => {
    if (event.key === "Tab") {
      document.documentElement.dataset.inputModality = "keyboard";
    }
  },
  true,
);

// The island reads its todos on first render, so the file of record is loaded
// into the running copy before anything mounts; other windows do not own it.
function render() {
  ReactDOM.createRoot(document.getElementById("root") as HTMLElement).render(
  <React.StrictMode>
    <I18nProvider>
      {windowKind === "translation" ? (
        <TranslationWindow />
      ) : windowKind === "settings" ? (
        <SettingsWindow />
      ) : (
        <IslandApp />
      )}
    </I18nProvider>
  </React.StrictMode>,
  );
}

if (windowKind === null) {
  void bootTodoFile()
    .catch(() => null)
    .then(render);
} else {
  render();
}
