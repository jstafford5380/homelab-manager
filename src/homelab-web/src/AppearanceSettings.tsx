import { useId, useState, useSyncExternalStore } from "react";
import { Card } from "@/components/ui/card";
import { Label } from "@/components/ui/label";
import { getAppearance, setAppearance, subscribeAppearance, type Appearance } from "./appearance";

export function AppearanceSettings() {
  const id = useId();
  const appearance = useSyncExternalStore(subscribeAppearance, getAppearance);
  const [message, setMessage] = useState("");
  return <Card className="appearance-settings">
    <h2>Appearance</h2>
    <p className="muted" id={`${id}-help`}>Choose a comfortable reading mode. Applies immediately and is saved in this browser. System follows your device’s light or dark setting.</p>
    <div className="field">
      <Label htmlFor={id}>Color theme</Label>
      <select id={id} value={appearance} aria-describedby={`${id}-help`}
        onChange={e => {
          const saved = setAppearance(e.target.value as Appearance);
          setMessage(saved ? "Appearance saved for this browser." : "Appearance applied for this session. Browser storage is unavailable.");
        }}>
        <option value="system">System</option>
        <option value="light">Light</option>
        <option value="dark">Dark</option>
      </select>
    </div>
    {message && <p role="status" className="muted">{message}</p>}
  </Card>;
}
