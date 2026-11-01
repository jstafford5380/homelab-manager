export type Appearance = "system" | "light" | "dark";
const key = "homelab.appearance";
const event = "homelab-appearance-change";
let preference: Appearance = "system";
let initialized = false;
const valid = (value: string | null): Appearance =>
  value === "light" || value === "dark" ? value : "system";
function apply() {
  document.documentElement.dataset.theme = preference === "system"
    ? window.matchMedia("(prefers-color-scheme: dark)").matches ? "dark" : "light"
    : preference;
}
export function initializeAppearance() {
  if (initialized) return;
  initialized = true;
  try { preference = valid(localStorage.getItem(key)); } catch { /* Storage can be disabled; system preference still works. */ }
  apply();
  window.matchMedia("(prefers-color-scheme: dark)").addEventListener("change", apply);
  window.addEventListener("storage", change => {
    if (change.key !== key && change.key !== null) return;
    try { preference = valid(localStorage.getItem(key)); } catch { return; }
    apply();
    window.dispatchEvent(new Event(event));
  });
}
export function setAppearance(value: Appearance): boolean {
  preference = value;
  let saved = true;
  try { localStorage.setItem(key, value); } catch { saved = false; }
  apply();
  window.dispatchEvent(new Event(event));
  return saved;
}
export const getAppearance = () => preference;
export function subscribeAppearance(listener: () => void) {
  window.addEventListener(event, listener);
  return () => window.removeEventListener(event, listener);
}
