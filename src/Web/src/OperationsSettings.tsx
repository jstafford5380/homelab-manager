import { useEffect, useState, type ReactNode } from "react";
import { Download } from "lucide-react";
import { Card } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { useDisplayFormat, type DisplayPreferences } from "./displayPreferences";
import { AppearanceSettings } from "./AppearanceSettings";
import { SettingsSaveBar } from "./SettingsSaveBar";

type Settings = DisplayPreferences & {
  hardwareCpuPercent: number;
  hardwareMemoryPercent: number;
  hardwareTemperatureC: number;
  hardwareSustainedMinutes: number;
  inventoryMinutes: number;
  driveAgeReviewYears: number;
  warrantyNoticeDays: number;
  smartMinutes: number;
  temperatureReviewCelsius: number | null;
  temperaturePolicyReason: string;
  readCachedTemperatures: boolean;
  readApiPowerOnHours: boolean;
  readDatasets: boolean;
  sampleRetentionDays: number;
  backupsEnabled: boolean;
  backupDailyCopies: number;
  backupWeeklyCopies: number;
};
type Draft = { [K in keyof Settings]: Settings[K] extends number | null ? Settings[K] | "" : Settings[K] };
type NumberKey = { [K in keyof Settings]-?: Settings[K] extends number | null ? K : never }[keyof Settings];
type ToggleKey = { [K in keyof Settings]-?: Settings[K] extends boolean ? K : never }[keyof Settings];
type Section = "monitoring" | "review-levels" | "data" | "display";
type PowerOnHoursRun = { atUtc: string; outcome: string; hoursAvailable: number | null; drives: number | null };
export type SettingsEnvelope = {
  settings: Settings;
  revisions: Record<Section, string>;
  backupDestinationConfigured: boolean;
  backupDestinationPath?: string;
  powerOnHoursLastRun?: PowerOnHoursRun | null;
};
type Backup = { id: string; createdUtc: string; fileName: string; status: string; sizeBytes: number };
type RunResult = { name: string; completed: boolean; hoursAvailable: number; unsupported: number; denied: number; unavailable: number };

const titles = { monitoring: "Monitoring", "review-levels": "Review levels", data: "Data & backups", display: "Display" };
const cleanup = "Older pool readings and collection attempts become daily summaries with ranges, peaks, and reading counts, kept for two years. Manual drive evidence and incidents remain unchanged. Cleanup checks hourly and creates a backup before removing details, even if daily backups are off. Without a working backup destination, cleanup waits and history continues growing. Lowering the history window applies at the next cleanup. Increasing it cannot recover removed details.";
const planning = "Age and warranty thresholds create planning reminders only. They do not predict failure or recommend replacement based on age alone.";

export function OperationsSettings({ section, saved, onSaved, onDirty }: {
  section: Section;
  saved: SettingsEnvelope;
  onSaved: (result: SettingsEnvelope) => void;
  onDirty: (dirty: boolean) => void;
}) {
  const { time, bytes } = useDisplayFormat();
  const [edits, setEdits] = useState<Partial<Draft>>({});
  const [errors, setErrors] = useState<Record<string, string[]>>({});
  const [error, setError] = useState("");
  const [conflict, setConflict] = useState(false);
  const [busy, setBusy] = useState(false);
  const [actionBusy, setActionBusy] = useState(false);
  const [actionMessage, setActionMessage] = useState("");
  const [message, setMessage] = useState("");
  const [backups, setBackups] = useState<Backup[] | null>(null);
  const [historyError, setHistoryError] = useState("");
  const [run, setRun] = useState(saved.powerOnHoursLastRun);
  const settings = { ...saved.settings, ...edits };
  const count = Object.keys(edits).length;

  useEffect(() => { onDirty(count > 0); }, [count, onDirty]);
  useEffect(() => () => onDirty(false), [onDirty]);
  useEffect(() => { if (error && !busy) document.getElementById(Object.keys(errors)[0])?.focus(); }, [error, busy, errors]);

  function change<K extends keyof Settings>(key: K, value: Draft[K]) {
    setMessage("");
    setErrors(current => {
      if (!current[key]) return current;
      const next = { ...current };
      delete next[key];
      return next;
    });
    setEdits(current => {
      const next = { ...current, [key]: value };
      if (Object.is(value, saved.settings[key])) delete next[key];
      return next;
    });
  }
  function discard() {
    setEdits({});
    setErrors({});
    if (!conflict) setError("");
  }
  async function save() {
    setBusy(true);
    setError("");
    setErrors({});
    try {
      const invalid: Record<string, string[]> = {};
      if (edits.temperatureReviewCelsius === "") invalid.temperatureReviewCelsius = ["Enter a Celsius review limit."];
      if (edits.timezone !== undefined) {
        try { new Intl.DateTimeFormat(undefined, { timeZone: edits.timezone }); }
        catch { invalid.timezone = ["Choose a valid timezone."]; }
      }
      if (Object.keys(invalid).length) {
        setErrors(invalid);
        throw new Error("Not saved. Fix the highlighted field and try again.");
      }
      const changes = Object.fromEntries(Object.entries(edits).map(([key, value]) =>
        [key, value === "" && typeof saved.settings[key as keyof Settings] === "number" ? null : value]));
      const response = await fetch(`/api/v1/settings/${section}`, {
        method: "PATCH",
        headers: { "Content-Type": "application/json", "X-Homelab-Request": "1" },
        body: JSON.stringify({ revision: saved.revisions?.[section], changes }),
      });
      const result = await response.json().catch(() => ({}));
      if (!response.ok) {
        setConflict(response.status === 409);
        if (result.errors) setErrors(result.errors);
        throw new Error(result.errors ? "Not saved. Fix the highlighted field and try again." : result.error ?? "Settings could not be saved.");
      }
      onSaved({ ...saved, ...result });
      discard();
      setMessage("Settings saved.");
    } catch (error) {
      setError(error instanceof Error ? error.message : String(error));
    } finally { setBusy(false); }
  }
  async function reload() {
    setBusy(true);
    try {
      const response = await fetch("/api/v1/settings");
      if (!response.ok) throw new Error("Could not reload saved settings.");
      const result: SettingsEnvelope = await response.json();
      setEdits(Object.fromEntries(Object.entries(edits).filter(([key, value]) =>
        !Object.is(result.settings[key as keyof Settings], value))));
      onSaved(result);
      setConflict(false);
      setError("");
    } catch (error) { setError(String(error)); }
    finally { setBusy(false); }
  }
  async function loadBackups(signal?: AbortSignal) {
    const response = await fetch("/api/v1/backups", { signal });
    if (!response.ok) throw new Error("Could not load backup history.");
    setBackups(await response.json());
    setHistoryError("");
  }
  useEffect(() => {
    if (section !== "data") return;
    const controller = new AbortController();
    void loadBackups(controller.signal).catch(error => {
      if (!controller.signal.aborted) setHistoryError(error.message);
    });
    return () => controller.abort();
  }, [section]);
  async function runAction(path: string) {
    setActionBusy(true);
    setActionMessage(path === "backups" ? "Creating backup…" : "Reading power-on hours…");
    try {
      const response = await fetch("/api/v1/" + path, { method: "POST", headers: { "X-Homelab-Request": "1" } });
      const result = await response.json().catch(() => null);
      if (!response.ok) throw new Error(result?.error ?? "The request failed. Try again.");
      if (path === "backups") {
        setActionMessage("Backup created at the configured server destination.");
        await loadBackups().catch(error => setHistoryError(error.message));
      } else {
        const servers: RunResult[] = result.servers;
        setActionMessage(servers.map(server => server.completed
          ? `${server.name}: ${server.hoursAvailable} drives returned hours. ${server.unsupported} unsupported, ${server.denied} denied, ${server.unavailable} unavailable.`
          : `${server.name}: no new reading was saved.`).join(" ") || "No active systems were available to read.");
        const latest = await fetch("/api/v1/settings");
        if (latest.ok) setRun((await latest.json()).powerOnHoursLastRun);
      }
    } catch (error) { setActionMessage(error instanceof Error ? error.message : String(error)); }
    finally { setActionBusy(false); }
  }
  function row(key: keyof Settings, label: string, control: ReactNode, hint?: string, explanation?: string, unit?: string) {
    const previous = saved.settings[key];
    const was = typeof previous === "boolean" ? previous ? "On" : "Off" : previous ?? "Off";
    return <div className="settings-row" key={key}>
      <div><label htmlFor={key}>{label}</label>{hint && <p className="muted">{hint}</p>}</div>
      <div>{control}
        {Object.hasOwn(edits, key) && <p className="muted">Was {was}{unit && previous != null ? ` ${unit}` : ""}</p>}
        {errors[key] && <p className="error" id={key + "-error"}>{errors[key].join(" ")}</p>}
      </div>
      {explanation && <details><summary>How this works</summary><p>{explanation}</p></details>}
    </div>;
  }
  function control(key: keyof Settings) {
    return { id: key, disabled: busy, "aria-invalid": !!errors[key], "aria-describedby": errors[key] ? key + "-error" : undefined };
  }
  function number(key: NumberKey, label: string, unit: string, hint?: string, explanation?: string) {
    return row(key, label, <div className="settings-number"><Input {...control(key)} type="number" step="any" value={settings[key] ?? ""}
      onChange={event => change(key, event.target.value === "" ? "" : event.target.valueAsNumber)} />
      <span className="muted">{unit}</span></div>, hint, explanation, unit);
  }
  function toggle(key: ToggleKey, label: string, hint: string, explanation?: string, disabled = false) {
    return row(key, label, <input {...control(key)} type="checkbox" role="switch" disabled={disabled || busy}
      checked={!!settings[key]} onChange={event => change(key, event.target.checked)} />, hint, explanation);
  }
  function group(title: string, children: ReactNode, action?: ReactNode) {
    return <Card className="settings-group"><div className="settings-group-heading"><h3>{title}</h3>{action}</div>{children}</Card>;
  }

  return <form noValidate className="settings-form" onSubmit={event => { event.preventDefault(); if (count && !busy && !conflict) void save(); }}>
    {section === "monitoring" && <>
      {group("Collection intervals", <>
        {number("inventoryMinutes", "Disk inventory interval", "minutes", "How often drive inventory is read.", "Disk inventory runs no faster than the connection’s pool and alert polling interval. Skipped polls do not refresh drive timestamps.")}
        {number("smartMinutes", "Drive evidence review interval", "minutes", "Sets the interval used to judge freshness.", "Drive measurements become stale after three intervals, measured from observation time. Uploading evidence does not reset this clock. This setting does not schedule NAS commands or tests.")}
      </>)}
      {group("Optional reads", <>
        {toggle("readDatasets", "Read dataset space usage", "Collect space totals for up to 256 datasets and volumes.", "Optional read on each connection cycle. Saves the latest names and space usage without scanning files or changing the NAS. Larger or unsupported responses retain the previous snapshot. Check compatibility with your installed TrueNAS 24.10 patch before enabling.")}
        {toggle("readApiPowerOnHours", "Read power-on hours through the TrueNAS API", "Read ATA SMART hour counters.", "Reads ATA SMART attributes every six hours, for up to 64 drives per server. These reads can wake sleeping drives and do not start self-tests. NVMe and SAS drives are unsupported by this API. Missing or ambiguous hour counters stay unknown, and permission failures are shown separately.")}
        <div className="settings-row"><div>
          <p>{run ? `Last recorded run ${time(run.atUtc)} · ${run.drives == null ? "failed" : `${run.hoursAvailable} of ${run.drives} drives reported hours (${run.outcome})`}` : "Never run"}</p>
          {!saved.settings.readApiPowerOnHours && <p className="muted">Save settings to enable Run now.</p>}
        </div><Button type="button" variant="outline" disabled={actionBusy || !saved.settings.readApiPowerOnHours}
          onClick={() => void runAction("storage/power-on-hours/run")}>{actionBusy ? "Running…" : "Run now"}</Button></div>
        {toggle("readCachedTemperatures", "Read existing NAS temperature cache", "Read temperatures the NAS already has in its cache.", "Optional cache-only read on each connection cycle. It does not start fresh drive queries or tests. Cached values lack observation times and verified physical-drive matches. They are displayed separately and do not affect drive-health findings. Check compatibility with your installed TrueNAS 24.10 patch before enabling.")}
      </>)}
    </>}
    {section === "review-levels" && <>
      {group("Drives", <>
        {number("driveAgeReviewYears", "Drive age review threshold", "years", "Flag older drives for planning review.", planning)}
        {number("warrantyNoticeDays", "Warranty notice window", "days", "Flag warranties approaching expiry.", planning)}
        <div className="settings-row"><div><label htmlFor="temperature-policy">Local temperature policy</label><p className="muted">Apply your own review limit to imported temperatures.</p></div>
          <input id="temperature-policy" type="checkbox" role="switch" disabled={busy} checked={settings.temperatureReviewCelsius != null}
            onChange={event => change("temperatureReviewCelsius", event.target.checked ? saved.settings.temperatureReviewCelsius ?? "" : null)} />
          <details><summary>How this works</summary><p>The limit applies to new imported temperature evidence for all tracked drives, including historical reports. It does not read sensors, change NAS settings, or define a universal safe temperature. Existing findings are retained when the policy changes.</p></details>
        </div>
        {settings.temperatureReviewCelsius != null && <>
          {number("temperatureReviewCelsius", "Temperature review limit", "°C")}
          {row("temperaturePolicyReason", "Temperature policy basis", <Input {...control("temperaturePolicyReason")} maxLength={2000}
            value={settings.temperaturePolicyReason ?? ""} onChange={event => change("temperaturePolicyReason", event.target.value)} />,
            "Required. Record your local operating policy or applicable manufacturer guidance.")}
        </>}
      </>)}
      {group("Hardware review levels", <>
        {number("hardwareCpuPercent", "CPU review level", "%", "Changes apply to charts and component statuses.")}
        {number("hardwareMemoryPercent", "Memory review level", "%", "Use by apps excludes cache.")}
        {number("hardwareTemperatureC", "Hardware temperature review level", "°C")}
        {number("hardwareSustainedMinutes", "Sustained use", "minutes per day")}
      </>)}
    </>}
    {section === "display" && <Card className="settings-group">
      <AppearanceSettings />
      {row("timezone", "Display timezone", <Input {...control("timezone")} value={settings.timezone} onChange={event => change("timezone", event.target.value)} />, "Used for every date and time shown.")}
      <fieldset className="settings-row" aria-describedby={errors.capacityUnits ? "capacityUnits-error" : undefined}>
        <legend className="sr-only">Capacity units</legend>
        <strong>Capacity units</strong>
        <div><div className="settings-options">
          {(["binary", "decimal"] as const).map((units, index) => <label key={units}>
            <input {...control("capacityUnits")} id={index === 0 ? "capacityUnits" : "decimal-units"} type="radio"
              name="capacityUnits" checked={settings.capacityUnits === units} onChange={() => change("capacityUnits", units)} />
            {units === "binary" ? "Binary (TiB)" : "Decimal (TB)"}
          </label>)}
        </div>
        {Object.hasOwn(edits, "capacityUnits") && <p className="muted">Was {saved.settings.capacityUnits === "binary" ? "Binary (TiB)" : "Decimal (TB)"}</p>}
        {errors.capacityUnits && <p className="error" id="capacityUnits-error">{errors.capacityUnits.join(" ")}</p>}</div>
      </fieldset>
    </Card>}
    {section === "data" && <>
      {group("History retention", <>
        {number("sampleRetentionDays", "Detailed monitoring history", "days", "Keep individual monitoring samples for this long.", cleanup)}
      </>)}
      {group("Application backups", <>
        <div className="settings-row"><div><strong>Destination</strong><p className="muted">Set on the server with Homelab__BackupPath.</p></div>
          <p className="settings-meta">{saved.backupDestinationPath ?? (saved.backupDestinationConfigured ? "Configured on the server" : "No backup destination configured")}</p>
          <details><summary>How this works</summary><p>Use an independent backup directory outside the live data directory. Homelab Control cannot verify this is on separate hardware. Verify a restore on another host. Default restore requires credential re-enrollment.</p></details>
        </div>
        {toggle("backupsEnabled", "Daily backups", "Retries every five minutes after a failed attempt.", undefined, !saved.backupDestinationConfigured)}
        {number("backupDailyCopies", "Daily backup copies", "copies")}
        {number("backupWeeklyCopies", "Weekly backup copies", "copies")}
      </>, saved.backupDestinationConfigured && <Button type="button" variant="outline" disabled={actionBusy} onClick={() => void runAction("backups")}><Download size={16} /> Back up now</Button>)}
      {group("Backup history", historyError ? <p role="alert" className="settings-row">{historyError}</p> : backups?.length ? backups.map(backup =>
        <div className="settings-row settings-meta" key={backup.id}><div>{time(backup.createdUtc)}<p className="muted">{backup.fileName}</p></div><span>{backup.status} · {bytes(backup.sizeBytes)}</span></div>
      ) : <p className="settings-row">{backups ? "No backups yet." : "Loading backup history…"}</p>)}
    </>}
    {actionMessage && <p role="status">{actionMessage}</p>}
    {message && <p role="status">{message}</p>}
    {(count > 0 || conflict) && <SettingsSaveBar section={titles[section]} count={count} error={error} busy={busy}
      onDiscard={discard} onReload={conflict ? () => void reload() : undefined} />}
  </form>;
}
