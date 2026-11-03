import { Monitor, Moon, Sun } from "lucide-react";
import { useId, useState, useSyncExternalStore } from "react";
import { getAppearance, setAppearance, subscribeAppearance } from "./appearance";

export function AppearanceSettings() {
  const id = useId();
  const appearance = useSyncExternalStore(subscribeAppearance, getAppearance);
  const [message, setMessage] = useState("");
  return <fieldset className="settings-row" aria-describedby={`${id}-help`}>
      <legend className="sr-only">Color theme</legend>
      <div><strong>Color theme</strong><p className="muted" id={`${id}-help`}>Applies immediately in this browser. System follows your device’s light or dark setting.</p></div>
      <div className="settings-options">{([["system", Monitor], ["light", Sun], ["dark", Moon]] as const).map(([theme, Icon]) =>
        <label key={theme}><input type="radio" name={id} value={theme} checked={appearance === theme}
          onChange={() => {
            const saved = setAppearance(theme);
            setMessage(saved ? "Appearance saved for this browser." : "Appearance applied for this session. Browser storage is unavailable.");
          }} /><Icon size={14} aria-hidden="true" />{theme[0].toUpperCase() + theme.slice(1)}</label>)}</div>
      {message && <p role="status" className="muted">{message}</p>}
    </fieldset>;
}
