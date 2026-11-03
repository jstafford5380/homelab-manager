import {
  useDisplayFormat,
  type DisplayPreferences,
} from "./displayPreferences";
import { useEffect, useState } from "react";
import { Card } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Download, Save } from "lucide-react";

type Settings = {
  hardwareCpuPercent?: number;
  hardwareMemoryPercent?: number;
  hardwareTemperatureC?: number;
  hardwareSustainedMinutes?: number;
  id: number;
  timezone: string;
  capacityUnits: "binary" | "decimal";
  inventoryMinutes: number;
  driveAgeReviewYears: number;
  warrantyNoticeDays: number;
  smartMinutes: number;
  temperatureReviewCelsius?: number | null;
  temperaturePolicyReason?: string;
  readCachedTemperatures: boolean;
  readApiPowerOnHours?: boolean;
  readDatasets?: boolean;
  sampleRetentionDays: number;
  rawRetentionDays: number;
  backupsEnabled: boolean;
  backupDailyCopies: number;
  backupWeeklyCopies: number;
};
type Backup = {
  id: string;
  createdUtc: string;
  fileName: string;
  status: string;
  sizeBytes: number;
};
export function OperationsSettings({
  onSaved,
}: {
  onSaved: (settings: DisplayPreferences) => void;
}) {
  const { bytes, time } = useDisplayFormat();
  const [settings, setSettings] = useState<Settings | null>(null);
  const [configured, setConfigured] = useState(false);
  const [backups, setBackups] = useState<Backup[]>([]);
  const [message, setMessage] = useState("");
  const [busy, setBusy] = useState(false);
  const [savedPowerOnEnabled, setSavedPowerOnEnabled] = useState(false);
  const [hoursBusy, setHoursBusy] = useState(false);
  const [hoursMessage, setHoursMessage] = useState("");
  async function runPowerOnHours() {
    setHoursBusy(true);
    setHoursMessage("Reading power-on hours…");
    try {
      const response = await fetch("/api/v1/storage/power-on-hours/run", {
        method: "POST", headers: { "X-Homelab-Request": "1" },
      });
      const data = await response.json().catch(() => null);
      if (!response.ok || !Array.isArray(data?.servers)) throw new Error(data?.error || "Power-on hours could not be read. Try again.");
      setHoursMessage(data.servers.map((s: { name: string; completed: boolean; hoursAvailable: number; unsupported: number; denied: number; unavailable: number }) =>
        s.completed ? `${s.name}: finished. ${s.hoursAvailable} drives returned hours; ${s.unsupported} unsupported; ${s.denied} permission denied; ${s.unavailable} unavailable.` :
          `${s.name}: no new reading was saved. Check the connection and collection status.`).join(" "));
    } catch (error) {
      setHoursMessage(error instanceof Error ? error.message : "Power-on hours could not be read. Try again.");
    } finally { setHoursBusy(false); }
  }
  async function load() {
    const response = await fetch("/api/v1/settings");
    if (!response.ok) throw new Error("Could not load settings");
    const data = await response.json();
    setSettings(data.settings);
    setSavedPowerOnEnabled(data.settings.readApiPowerOnHours ?? false);
    setConfigured(data.backupDestinationConfigured);
    const history = await fetch("/api/v1/backups");
    if (!history.ok) throw new Error("Could not load backups");
    setBackups(await history.json());
  }
  useEffect(() => {
    void load().catch((e) => setMessage(String(e)));
  }, []);
  async function submit(path: string, method: string, body: unknown) {
    setBusy(true);
    setMessage("");
    try {
      if (path === "settings") {
        try {
          new Intl.DateTimeFormat(undefined, {
            timeZone: (body as Settings).timezone,
          });
        } catch {
          throw new Error("Choose a valid timezone.");
        }
      }
      const response = await fetch("/api/v1/" + path, {
        method,
        headers: {
          "Content-Type": "application/json",
          "X-Homelab-Request": "1",
        },
        body: JSON.stringify(body),
      });
      if (!response.ok) {
        const error = await response.json();
        throw new Error(error.error || error.detail || "Request failed");
      }
      if (path === "settings") {
        const saved: Settings = await response.json();
        setSettings(saved);
        setSavedPowerOnEnabled(saved.readApiPowerOnHours ?? false);
        onSaved(saved);
        setMessage("Settings saved.");
      } else {
        await load();
        setMessage(
          "Consistent backup created at the configured server destination.",
        );
      }
    } catch (error) {
      setMessage(error instanceof Error ? error.message : String(error));
    } finally {
      setBusy(false);
    }
  }
  if (!settings)
    return <p className="muted">{message || "Loading operating settings…"}</p>;
  return (
    <section style={{ marginTop: 32 }}>
      <div className="section-title">
        <h2>Operating settings & application backups</h2>
      </div>
      <Card className="list-card">
        <form
          style={{ padding: "16px 0" }}
          onSubmit={(event) => {
            event.preventDefault();
            void submit("settings", "PUT", settings);
          }}
        >
          <div className="settings-grid">
            <div className="field">
              <Label htmlFor="timezone">Display timezone</Label>
              <Input
                id="timezone"
                value={settings.timezone}
                onChange={(e) =>
                  setSettings({ ...settings, timezone: e.target.value })
                }
              />
            </div>
            <div className="field">
              <Label htmlFor="capacityUnits">Capacity units</Label>
              <select
                id="capacityUnits"
                value={settings.capacityUnits}
                onChange={(e) =>
                  setSettings({
                    ...settings,
                    capacityUnits: e.target.value as "binary" | "decimal",
                  })
                }
              >
                <option value="binary">Binary (TiB, GiB)</option>
                <option value="decimal">Decimal (TB, GB)</option>
              </select>
            </div>
            {(
              [
                {
                  key: "inventoryMinutes",
                  label: "Disk inventory interval (minutes)",
                  min: 1,
                  max: 1440,
                },
                {
                  key: "smartMinutes",
                  label: "Drive evidence review interval (minutes)",
                  min: 15,
                  max: 10080,
                },
                {
                  key: "driveAgeReviewYears",
                  label: "Drive age review threshold (years)",
                  min: 1,
                  max: 30,
                },
                {
                  key: "warrantyNoticeDays",
                  label: "Warranty notice window (days)",
                  min: 0,
                  max: 730,
                },
                {
                  key: "sampleRetentionDays",
                  label: "Detailed monitoring history (days)",
                  min: 7,
                  max: 3650,
                },
                {
                  key: "backupDailyCopies",
                  label: "Daily backup copies",
                  min: 1,
                  max: 90,
                },
                {
                  key: "backupWeeklyCopies",
                  label: "Weekly backup copies",
                  min: 1,
                  max: 52,
                },
              ] as const
            ).map((field) => (
              <div className="field" key={field.key}>
                <Label htmlFor={field.key}>{field.label}</Label>
                <Input
                  id={field.key}
                  type="number"
                  min={field.min}
                  max={field.max}
                  value={settings[field.key]}
                  onChange={(e) =>
                    setSettings({
                      ...settings,
                      [field.key]: Number(e.target.value),
                    })
                  }
                />
              </div>
            ))}
          </div>
          <fieldset id="hardware-review-levels" style={{ marginBlock: 16 }}>
            <legend>Hardware review levels</legend>
            <p className="muted">Use by apps excludes cache. These are your review levels, not predictions or manufacturer safety limits. Changes apply to charts and component statuses.</p>
            <div className="settings-grid">{([
              ["hardwareCpuPercent", "CPU review level (%)", 1, 100, 80],
              ["hardwareMemoryPercent", "Memory review level (%)", 1, 100, 80],
              ["hardwareTemperatureC", "Hardware temperature review level (°C)", -50, 200, 80],
              ["hardwareSustainedMinutes", "Sustained use (minutes per day)", 5, 1440, 60],
            ] as const).map(([key, label, min, max, fallback]) => <div className="field" key={key}><Label htmlFor={key}>{label}</Label><Input id={key} type="number" min={min} max={max} step={key === "hardwareSustainedMinutes" ? 1 : "any"} value={settings[key] ?? fallback} onChange={e => setSettings({ ...settings, [key]: Number(e.target.value) })} /></div>)}</div>
          </fieldset>
          <fieldset style={{ marginBlock: 16 }}>
            <legend>Optional local temperature policy</legend>
            <p className="muted">Blank means disabled. This review limit applies to new imported temperature evidence for all tracked drives, including historical reports. It does not read sensors, change NAS settings, or define a universal safe temperature. Existing findings are retained when the policy changes.</p>
            <div className="field">
              <Label htmlFor="temperatureReviewCelsius">Temperature review limit (°C)</Label>
              <Input id="temperatureReviewCelsius" type="number" min="-273.15" max="1000" step="any"
                value={settings.temperatureReviewCelsius ?? ""}
                onChange={e => setSettings({ ...settings, temperatureReviewCelsius: e.target.value === "" ? null : Number(e.target.value) })} />
            </div>
            <div className="field">
              <Label htmlFor="temperaturePolicyReason">Temperature policy basis</Label>
              <Input id="temperaturePolicyReason" maxLength={2000}
                value={settings.temperaturePolicyReason ?? ""}
                onChange={e => setSettings({ ...settings, temperaturePolicyReason: e.target.value })} />
              <small>Record your local operating policy or applicable manufacturer guidance. Required when a limit is set.</small>
            </div>
          </fieldset>
          <p className="muted" style={{ marginBottom: 16 }}>
            Disk inventory uses its own interval, no faster than the
            connection’s pool/alert polling interval. Skipped inventory polls do
            not refresh drive timestamps. Age and warranty thresholds create
            planning reminders only. They do not predict failure or recommend
            replacement based on age alone. Drive measurements become stale
            after three evidence review intervals, using observation time, never
            upload time. This setting does not schedule NAS commands or tests.
          </p>
          <p className="muted" style={{ marginBottom: 16 }}>
            Older pool readings and collection attempts become daily summaries
            with ranges, peaks, and reading counts, kept for two years. Manual
            drive evidence and incidents remain unchanged. Cleanup checks hourly
            and creates a backup before removing details, even if daily backups
            are off. Without a working backup destination, cleanup waits and
            history continues growing. Lowering the history window applies at
            the next cleanup; increasing it cannot recover removed details.
          </p>
          <label className="backup-checkbox">
            <input type="checkbox" checked={settings.readDatasets ?? false}
              onChange={e => setSettings({ ...settings, readDatasets: e.target.checked })} />{" "}
            Read dataset space usage
          </label>
          <p className="muted" style={{ margin: "12px 0" }}>
            Optional read on each connection cycle, limited to 256 datasets and volumes.
            Saves the latest names and space usage, without scanning files or changing the NAS.
            Larger or unsupported responses retain the previous snapshot. Check compatibility
            with your installed TrueNAS 24.10 patch before enabling.
          </p>
          <div style={{ display: "flex", alignItems: "center", flexWrap: "wrap", gap: 12 }}>
            <label className="backup-checkbox">
              <input type="checkbox" checked={settings.readApiPowerOnHours ?? false}
                onChange={e => setSettings({ ...settings, readApiPowerOnHours: e.target.checked })} />{" "}
              Read power-on hours through the TrueNAS API
            </label>
            <Button type="button" variant="outline" size="sm"
              disabled={busy || hoursBusy || !savedPowerOnEnabled || !settings.readApiPowerOnHours}
              onClick={() => void runPowerOnHours()}>
              {hoursBusy ? "Running…" : "Run now"}
            </Button>
          </div>
          {settings.readApiPowerOnHours && !savedPowerOnEnabled && <p className="muted">Save settings to enable Run now.</p>}
          <p role="status" aria-live="polite" className="muted">{hoursMessage}</p>
          <p className="muted" style={{ margin: "12px 0" }}>
            Reads ATA SMART attributes every six hours, for up to 64 drives per server.
            These reads can wake sleeping drives. They do not start self-tests.
            NVMe and SAS drives are unsupported by this API. Missing or ambiguous
            hour counters stay unknown, and permission failures are shown separately.
          </p>
          <label className="backup-checkbox">
            <input
              type="checkbox"
              checked={settings.readCachedTemperatures ?? false}
              onChange={(e) =>
                setSettings({
                  ...settings,
                  readCachedTemperatures: e.target.checked,
                })
              }
            />{" "}
            Read existing NAS temperature cache
          </label>
          <p className="muted" style={{ margin: "12px 0" }}>
            Optional cache-only read on each connection cycle. It does not start
            fresh drive queries or tests. Cached values lack observation times
            and verified physical-drive matches; they are displayed separately
            and do not affect drive-health findings. Check compatibility with
            your installed TrueNAS 24.10 patch before enabling.
          </p>
          <label className="backup-checkbox">
            <input
              type="checkbox"
              checked={settings.backupsEnabled}
              disabled={!configured}
              onChange={(e) =>
                setSettings({ ...settings, backupsEnabled: e.target.checked })
              }
            />{" "}
            Create an application backup every 24 hours
          </label>
          <p className="muted" style={{ margin: "12px 0" }}>
            {configured
              ? "A server backup destination is configured. Keep that destination independent of the monitored NAS and verify a restore on another host."
              : "Set Homelab__BackupPath on the server to an independent backup directory outside the live data directory."}
          </p>
          <div style={{ display: "flex", gap: 12 }}>
            <Button disabled={busy}>
              <Save size={15} /> Save settings
            </Button>
            <Button
              type="button"
              variant="outline"
              disabled={busy || !configured}
              onClick={() => void submit("backups", "POST", {})}
            >
              <Download size={15} /> Back up now
            </Button>
          </div>
        </form>
      </Card>
      {message && (
        <p role="status" style={{ margin: "12px 0" }}>
          {message}
        </p>
      )}
      <div className="section-title" style={{ marginTop: 24 }}>
        <h2>Backup history</h2>
        <span className="muted">
          Default restore requires credential re-enrollment
        </span>
      </div>
      <Card className="list-card">
        {backups.length ? (
          backups.map((backup) => (
            <div className="row" key={backup.id}>
              <Download size={18} />
              <div>
                <h3>{time(backup.createdUtc)}</h3>
                <small>{backup.fileName}</small>
              </div>
              <span>{backup.status}</span>
              <small>{bytes(backup.sizeBytes)}</small>
            </div>
          ))
        ) : (
          <p className="muted" style={{ padding: 16 }}>
            No successful backups recorded yet.
          </p>
        )}
      </Card>
    </section>
  );
}
