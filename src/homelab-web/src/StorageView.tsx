import { ObservationTrend } from "./ObservationTrend";
import { DriveEvidenceView } from "./DriveEvidenceView";
import { useSearchParams } from "react-router";
import { lazy, Suspense, useEffect, useState } from "react";
import { Card } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Badge } from "@/components/ui/badge";
import { ReplacementHistory } from "./ReplacementHistory";
import { TopologyView } from "./TopologyView";
import { HardDrive, ArrowLeft, Plus, Save } from "lucide-react";

const SmartReportImport = lazy(() => import("./SmartReportImport").then(module => ({ default: module.SmartReportImport })));
const IdentityHistoryView = lazy(() => import("./IdentityHistoryView").then(module => ({ default: module.IdentityHistoryView })));
const DriveHardwareView = lazy(() => import("./DriveHardwareView").then(module => ({ default: module.DriveHardwareView })));
const DriveAcquisitionView = lazy(() => import("./DriveAcquisitionView"));
const DriveMaintenanceView = lazy(() => import("./DriveMaintenanceView").then(module => ({ default: module.DriveMaintenanceView })));

type Disk = {
  id: string;
  connectionId: string;
  lastSeenUtc: string;
  serial: string;
  model: string;
  alias: string;
  wwn: string | null;
  mappingConfidence: string;
  bay: string | null;
  bayVerified: boolean;
  bayEvidence: string | null;
  present: boolean;
  purchasedUtc: string | null;
  warrantyEndUtc: string | null;
  notes: string;
  lifecycle: string;
  purchaseDateConfidence: string;
};
type Detail = {
  disk: Disk;
  aliases: {
    id: number;
    path: string;
    firstSeenUtc: string;
    endedUtc: string | null;
  }[];
  findings: { id: string; title: string; evidence: string; state: string }[];
  plans: { id: string; title: string; status: string }[];
};
async function api(
  path: string,
  body?: unknown,
  method = "POST",
  signal?: AbortSignal,
) {
  const response = await fetch(
    "/api/v1" + path,
    body === undefined
      ? { signal }
      : {
          method,
          signal,
          headers: {
            "Content-Type": "application/json",
            "X-Homelab-Request": "1",
          },
          body: JSON.stringify(body),
        },
  );
  const result = await response.json().catch(() => null);
  if (!response.ok)
    throw new Error(
      result?.error ||
        (response.status === 404
          ? "This drive could not be found in this installation."
          : `Request failed (${response.status})`),
    );
  return result;
}
function Field({
  name,
  label,
  type = "text",
  value = "",
  required = false,
  step,
}: {
  name: string;
  label: string;
  type?: string;
  value?: string;
  required?: boolean;
  step?: string;
}) {
  return (
    <div className="field">
      <Label htmlFor={name}>{label}</Label>
      <Input
        id={name}
        name={name}
        type={type}
        step={step}
        defaultValue={value}
        required={required}
      />
    </div>
  );
}
const metrics = [
  "ata.pending_sectors",
  "ata.offline_uncorrectable",
  "ata.reallocated_sectors",
  "ata.crc_errors",
  "nvme.media_errors",
  "scsi.grown_defects",
  "scsi.read_uncorrected_errors",
  "scsi.write_uncorrected_errors",
  "scsi.verify_uncorrected_errors",
  "nvme.critical_warning",
  "nvme.available_spare",
  "nvme.available_spare_threshold",
  "nvme.percentage_used",
  "temperature",
  "power_on_hours",
  "power_cycles",
];
export function StorageView({
  disks,
  timezone,
  refresh,
  refreshRevision,
}: {
  disks: Disk[];
  timezone: string;
  refresh: () => Promise<void>;
  refreshRevision: number | null;
}) {
  const [searchParams, setSearchParams] = useSearchParams();
  const selected = searchParams.get("disk");
  const setSelected = (id: string | null) =>
    setSearchParams(id ? { disk: id } : {});
  const [detail, setDetail] = useState<Detail | null>(null);
  const [historyRevision, setHistoryRevision] = useState(0);
  const [error, setError] = useState("");
  const [importMetric, setImportMetric] = useState(metrics[0]);
  const importUnit =
    importMetric === "scsi.grown_defects" ? "defects" : importMetric === "nvme.critical_warning"
      ? "bitmask"
      : importMetric === "nvme.percentage_used"
        ? "% rated endurance"
        : importMetric === "nvme.available_spare" ||
            importMetric === "nvme.available_spare_threshold"
          ? "%"
          : importMetric.startsWith("ata.") && importMetric !== "ata.crc_errors"
            ? "sectors"
            : importMetric === "temperature"
              ? "°C"
              : importMetric === "power_on_hours"
                ? "hours"
                : importMetric === "power_cycles"
                  ? "cycles"
                  : "errors";
  const [metric, setMetric] = useState(metrics[0]);
  const [days, setDays] = useState(7);
  const [busy, setBusy] = useState(false);
  async function load(id: string, signal?: AbortSignal) {
    const record = await api("/disks/" + id, undefined, "GET", signal);
    if (signal?.aborted) return;
    setDetail(record);
  }
  useEffect(() => {
    const controller = new AbortController();
    setError("");
    if (selected)
      void load(selected, controller.signal).catch((e) => {
        if (!controller.signal.aborted) setError(String(e));
      });
    return () => controller.abort();
  }, [selected, refreshRevision]);
  useEffect(() => {
    window.scrollTo({ top: 0, behavior: "instant" });
  }, [selected]);
  async function save(path: string, body: unknown, method = "POST") {
    setBusy(true);
    setError("");
    try {
      await api(path, body, method);
      if (selected) await load(selected);
      if (path.endsWith("/observations")) setHistoryRevision(value => value + 1);
      await refresh();
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setBusy(false);
    }
  }
  const timestamp = (value: string | null) =>
    value
      ? new Date(value).toLocaleString(undefined, { timeZone: timezone })
      : "Unknown";
  if (selected && (!detail || detail.disk.id !== selected))
    return (
      <section>
        <Button variant="outline" onClick={() => setSelected(null)}>
          <ArrowLeft size={15} /> All drives
        </Button>
        <p
          className={error ? "error" : "muted"}
          role={error ? "alert" : "status"}
        >
          {error || "Loading drive evidence…"}
        </p>
      </section>
    );
  if (!selected || !detail)
    return (
      <>
        <TopologyView timezone={timezone} />
        <div className="section-title">
          <h2>Physical drives</h2>
          <Badge variant="secondary">{disks.length} tracked</Badge>
        </div>
        {error && (
          <p role="alert" className="error">
            {error}
          </p>
        )}
        <Card className="list-card">
          {disks.length ? (
            disks.map((d) => (
              <div className="row" key={d.id}>
                <HardDrive />
                <div>
                  <h3>{d.model || "Unknown model"}</h3>
                  <small>
                    Serial: {d.serial || "Unresolved"} · {d.alias}
                  </small>
                  <p className="muted">Last seen: {timestamp(d.lastSeenUtc)}</p>
                </div>
                <Badge variant="outline">
                  {d.mappingConfidence === "ambiguous" ? "Bay needs identity review" : d.bayVerified ? d.bay : "Bay unverified"}
                </Badge>
                <Badge variant="secondary">
                  {d.present ? "Present" : "Absent"}
                </Badge>
                <Button
                  variant="outline"
                  onClick={() => {
                    setDetail(null);
                    setSelected(d.id);
                  }}
                >
                  View drive
                </Button>
              </div>
            ))
          ) : (
            <div className="empty">
              <HardDrive />
              <h3>No disks discovered yet</h3>
              <p>
                Enroll a NAS to discover stable disk identifiers and preserve
                their history.
              </p>
            </div>
          )}
        </Card>
      </>
    );
  const disk = detail.disk;
  return (
    <>
      <Button
        variant="outline"
        onClick={() => {
          setSelected(null);
          setDetail(null);
        }}
      >
        <ArrowLeft size={15} /> All drives
      </Button>
      <div className="section-title" style={{ marginTop: 24 }}>
        <div>
          <h2>
            {disk.model} · {disk.serial}
          </h2>
          <p className="muted">
            WWN: {disk.wwn || "Unavailable"} · Identity:{" "}
            {disk.mappingConfidence}
          </p>
        </div>
        <Badge variant="outline">
          {disk.mappingConfidence === "ambiguous" ? "Bay needs identity review" : disk.bayVerified ? `Verified bay: ${disk.bay}` : "Bay unverified"}
        </Badge>
      </div>
      {error && (
        <p role="alert" className="error">
          {error}
        </p>
      )}
      <DriveEvidenceView
        key={`evidence:${disk.id}`}
        diskId={disk.id}
        revision={refreshRevision}
      />
      <div className="overview-grid">
        <section>
          <ObservationTrend key={`${disk.id}:${metric}:${days}:${historyRevision}`} diskId={disk.id} metric={metric} metrics={metrics} setMetric={setMetric} days={days} setDays={setDays} timezone={timezone} />
          <Card className="list-card" style={{ marginTop: 20 }}>
            <h2 style={{ marginTop: 16 }}>Findings & linked plans</h2>
            {detail.findings.map((f) => (
              <div className="row" key={f.id}>
                <div>
                  <h3>
                    {f.title} · {f.state}
                  </h3>
                  <small>{f.evidence}</small>
                </div>
              </div>
            ))}
            {detail.plans.map((p) => (
              <div className="row" key={p.id}>
                <div>{p.title}</div>
                <Badge variant="outline">{p.status}</Badge>
              </div>
            ))}
            <form
              style={{ padding: "20px 0" }}
              onSubmit={(e) => {
                e.preventDefault();
                const fields = Object.fromEntries(
                  new FormData(e.currentTarget),
                );
                void save("/plans", {
                  ...fields,
                  diskId: disk.id,
                  budget: fields.budget ? Number(fields.budget) : null,
                  currency: "USD",
                });
              }}
            >
              <Field name="title" label="Replacement or spare plan" required />
              <Field
                name="reason"
                label="Evidence and compatibility assumptions"
                required
              />
              <Field name="budget" label="Budget (USD)" type="number" />
              <Button disabled={busy}>
                <Plus size={15} /> Add linked plan
              </Button>
            </form>
          </Card>
        <Suspense fallback={<p className="muted">Loading identity history…</p>}><IdentityHistoryView key={disk.id} diskId={disk.id} /></Suspense>
        <Suspense fallback={<p className="muted">Loading shared hardware…</p>}><DriveHardwareView key={disk.id} disk={disk} /></Suspense>
        <Suspense fallback={<p className="muted">Loading maintenance history…</p>}><DriveMaintenanceView key={disk.id} disk={disk} days={days} /></Suspense>
        </section>
        <section>
          <Suspense fallback={<p>Loading acquisition notes…</p>}><DriveAcquisitionView key={disk.id} diskId={disk.id} timezone={timezone} /></Suspense>
          <Card className="list-card">
            <h2 style={{ marginTop: 16 }}>Drive notes & physical location</h2>
            <form
              key={disk.id + disk.bay + disk.bayVerified}
              style={{ padding: "20px 0" }}
              onSubmit={(e) => {
                e.preventDefault();
                const f = Object.fromEntries(new FormData(e.currentTarget));
                void save(
                  `/disks/${disk.id}/metadata`,
                  {
                    ...f,
                    bayVerified: f.bayVerified === "on",
                    purchasedUtc: f.purchasedUtc
                      ? new Date(String(f.purchasedUtc)).toISOString()
                      : null,
                    warrantyEndUtc: f.warrantyEndUtc
                      ? new Date(String(f.warrantyEndUtc)).toISOString()
                      : null,
                  },
                  "PUT",
                );
              }}
            >
              <Field name="bay" label="Bay label" value={disk.bay || ""} />
              <label className="backup-checkbox">
                <input
                  type="checkbox"
                  name="bayVerified"
                  defaultChecked={disk.bayVerified}
                />{" "}
                I verified the bay against physical evidence
              </label>
              <Field
                name="confirmedSerial"
                label="Retype the physical serial to verify"
              />
              <Field
                name="bayEvidence"
                label="Verification evidence"
                value={disk.bayEvidence || ""}
              />
              <Field
                name="purchasedUtc"
                label="Purchase date"
                type="date"
                value={disk.purchasedUtc?.slice(0, 10) || ""}
              />
              <div className="field">
                <Label htmlFor="purchaseDateConfidence">
                  Purchase-date confidence
                </Label>
                <select
                  id="purchaseDateConfidence"
                  name="purchaseDateConfidence"
                  defaultValue={disk.purchaseDateConfidence}
                >
                  <option value="unknown">Unknown</option>
                  <option value="estimated">Estimated</option>
                  <option value="documented">Documented</option>
                </select>
              </div>
              <Field
                name="warrantyEndUtc"
                label="Warranty end"
                type="date"
                value={disk.warrantyEndUtc?.slice(0, 10) || ""}
              />
              <div className="field">
                <Label htmlFor="lifecycle">Lifecycle</Label>
                <select
                  id="lifecycle"
                  name="lifecycle"
                  defaultValue={disk.lifecycle}
                >
                  <option value="installed">Installed</option>
                  <option value="spare">Spare</option>
                  <option value="retired">Retired</option>
                </select>
              </div>
              <Field name="notes" label="Notes" value={disk.notes} />
              <Button disabled={busy}>
                <Save size={15} /> Save metadata
              </Button>
            </form>
          </Card>
        </section>
      </div>
      <Card className="list-card">
        <Suspense fallback={<p>Loading report imports…</p>}><SmartReportImport
          disk={disk}
          onImported={async () => {
            await load(disk.id);
            setHistoryRevision(value => value + 1);
            await refresh();
          }}
        /></Suspense>
        <h2 style={{ marginTop: 16 }}>Import an existing observation</h2>
        <p className="muted">
          Record existing SMART evidence. This does not run a test or execute a
          command on the NAS.
        </p>
        <form
          style={{ padding: "20px 0" }}
          onSubmit={(e) => {
            e.preventDefault();
            const f = Object.fromEntries(new FormData(e.currentTarget));
            setMetric(String(f.metric));
            void save(`/disks/${disk.id}/observations`, {
              ...f,
              value: f.value === "" ? null : Number(f.value),
              observedUtc: f.observedUtc
                ? new Date(String(f.observedUtc)).toISOString()
                : null,
              quality:
                f.value === ""
                  ? "unknown"
                  : f.observedUtc
                    ? "observed"
                    : "unverified",
              sourceKey: crypto.randomUUID(),
            });
          }}
        >
          <div className="settings-grid">
            <div className="field">
              <Label htmlFor="metric">Observation metric</Label>
              <select
                id="metric"
                name="metric"
                value={importMetric}
                onChange={(e) => setImportMetric(e.target.value)}
              >
                {metrics.map((m) => (
                  <option key={m}>{m}</option>
                ))}
              </select>
            </div>
            <Field
              name="value"
              label="Value (leave empty if unknown)"
              type="number"
              step={importUnit === "°C" || importUnit === "hours" ? "any" : "1"}
            />
            <div className="field">
              <Label htmlFor="unit">Unit</Label>
              <Input id="unit" name="unit" value={importUnit} readOnly />
              <small>
                Convert source units explicitly before importing. A value
                without an observation time is recorded as unverified.
              </small>
            </div>
            <Field
              name="observedUtc"
              label="Observation time (your browser timezone)"
              type="datetime-local"
            />
            <Field name="source" label="Evidence source / reference" required />
            <Field
              name="sourceSchema"
              label="Metric schema / vendor interpretation"
              value="manual-v1"
              required
            />
            <Field
              name="resetEpoch"
              label="Counter continuity group (leave blank if unknown)"
            />
            <p className="muted">
              Use the same continuity group only when observations share the
              same device, source, and counter baseline. Change it after a
              reset/reboot or source change. Unknown continuity disables counter
              comparisons and automatic media-finding resolution.
            </p>
            <Field name="missingReason" label="Reason if unknown" />
          </div>
          <Button disabled={busy}>Import evidence</Button>
        </form>
      </Card>
      <ReplacementHistory
        key={`replacements:${disk.id}`}
        disk={disk}
        disks={disks}
        timezone={timezone}
        onSelect={(id) => {
          setDetail(null);
          setSelected(id);
        }}
      />
      <div className="section-title" style={{ marginTop: 24 }}>
        <h2>Device alias history</h2>
      </div>
      <Card className="list-card">
        {detail.aliases.map((a) => (
          <div className="row" key={a.id}>
            <strong>{a.path}</strong>
            <div>
              <small>
                {timestamp(a.firstSeenUtc)} →{" "}
                {a.endedUtc ? timestamp(a.endedUtc) : "Current"}
              </small>
            </div>
          </div>
        ))}
      </Card>
    </>
  );
}
