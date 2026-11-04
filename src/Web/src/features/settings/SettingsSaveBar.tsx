import {Button} from "@/components/ui/button";

export function SettingsSaveBar({ section, count, error, busy, onDiscard, onReload }: {
  section: string;
  count: number;
  error: string;
  busy: boolean;
  onDiscard: () => void;
  onReload?: () => void;
}) {
  return <div className="settings-savebar" role="region" aria-label="Unsaved changes" data-error={!!error}>
    <div>
      <strong>{count} unsaved {count === 1 ? "change" : "changes"} in {section}</strong>
      {error && <p className="error" role="alert">{error}</p>}
    </div>
    <div className="settings-actions">
      {onReload && <Button type="button" variant="outline" onClick={onReload} disabled={busy}>Reload saved settings</Button>}
      <Button type="button" variant="outline" onClick={onDiscard} disabled={busy}>Discard</Button>
      <Button type="submit" disabled={busy || !!onReload}>{busy ? "Saving…" : "Save changes"}</Button>
    </div>
  </div>;
}
