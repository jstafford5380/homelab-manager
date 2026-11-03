import { useDisplayFormat } from "./displayPreferences";
import { lazy, Suspense, useEffect, useRef, useState } from "react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogDescription,
} from "@/components/ui/dialog";
import type { SelfTestLog } from "./AtaSelfTestView";
const AtaSelfTestView = lazy(() => import("./AtaSelfTestView"));
type Reading = {
  metric: string;
  value: number | null;
  unit: string;
  quality: string;
  missingReason: string | null;
};
type Attribute = {
  id: number;
  name: string | null;
  rawNumeric: string | null;
  rawText: string | null;
  normalizedValue: number | null;
  worst: number | null;
  threshold: number | null;
  whenFailed: string | null;
};
type Preview = {
  reportHash: string;
  ataSelfTests?: SelfTestLog[] | null;
  deviceInfo?: { firmware: string | null; firmwareField: string; interface: string | null; interfaceField: string; quality: string } | null;
  overallSmart?: { passed: boolean | null; quality: string } | null;
  protocol: string;
  toolVersion: string;
  serial: string;
  model: string;
  observedUtc: string | null;
  sourceSchema: string;
  readings: Reading[];
  attributes: Attribute[];
  warnings: string[];
  continuityGroup: string;
};
type Saved = {
  id: string;
  actor: string;
  importedUtc: string;
  report: Preview;
};
const labels: Record<string, string> = {
  "ata.pending_sectors": "Pending sectors",
  "ata.offline_uncorrectable": "Offline uncorrectable",
  "ata.reallocated_sectors": "Reallocated sectors",
  "ata.crc_errors": "Transport CRC errors",
  "nvme.critical_warning": "NVMe warning flags",
  "nvme.available_spare": "NVMe spare capacity",
  "nvme.available_spare_threshold": "NVMe spare warning threshold",
  "nvme.percentage_used": "NVMe rated endurance used",
  "scsi.read_uncorrected_errors": "SCSI uncorrected read errors",
  "scsi.write_uncorrected_errors": "SCSI uncorrected write errors",
  "scsi.verify_uncorrected_errors": "SCSI uncorrected verify errors",
  "scsi.grown_defects": "SCSI grown defects",
  "nvme.media_errors": "NVMe media/data-integrity errors",
  temperature: "Temperature",
  power_on_hours: "Power-on hours",
  power_cycles: "Power cycles",
};
function ReportDetails({ report }: { report: Preview }) {
  const { time } = useDisplayFormat();
  return (
    <>
      <p>
        <strong>{report.model}</strong> · Serial {report.serial}
      </p>
      <p className="muted">
        {report.protocol} · smartctl {report.toolVersion} · Report time:{" "}
        {time(report.observedUtc)}
      </p>
      <details className="report-reading">
        <summary>Firmware and interface evidence</summary>
        <p>Firmware / revision: {report.deviceInfo?.firmware || "Unknown"}</p>
        <p>Reported interface / standard: {report.deviceInfo?.interface || "Unknown"}</p>
        <small>{report.deviceInfo?.quality || "unknown"}{report.deviceInfo && ` · Source fields: ${report.deviceInfo.firmwareField}; ${report.deviceInfo.interfaceField}`}</small>
        <p className="muted">Applies to this report time. Missing details remain unknown, including in older imports. A protocol or standard version does not prove connector, enclosure compatibility, link speed or current firmware. No update is performed.</p>
      </details>
      <div className="report-reading">
        <strong>Overall SMART assessment</strong>
        <p>{report.overallSmart?.passed === true ? "Reported passed" : report.overallSmart?.passed === false ? "Reported failed" : "Unknown"}</p>
        <small>{report.overallSmart?.quality || "unknown"} · Applies to this report time, not current drive health.</small>
        <p className="muted">A passed result does not override adverse readings or establish recoverability. This is not a self-test completion record.</p>
      </div>
      {report.protocol === "ATA" && <Suspense fallback={<p>Loading recorded self-tests…</p>}><AtaSelfTestView logs={report.ataSelfTests} /></Suspense>}
      <div className="report-reading-grid">
        {report.readings.map((reading) => (
          <div className="report-reading" key={reading.metric}>
            <strong>{labels[reading.metric] || reading.metric}</strong>
            <p>
              {reading.value === null
                ? "Unknown"
                : `${reading.value.toLocaleString()} ${reading.unit}`}
            </p>
            <small>
              {reading.quality}
              {reading.missingReason ? ` · ${reading.missingReason}` : ""}
            </small>
          </div>
        ))}
      </div>
      <ul className="report-warnings">
        {report.warnings.map((warning) => (
          <li key={warning}>{warning}</li>
        ))}
      </ul>
      <details>
        <summary>Raw attribute and source details</summary>
        <p className="muted">
          Schema: {report.sourceSchema}. Continuity:{" "}
          {report.continuityGroup || "unknown"}.
        </p>
        {report.attributes.map((attribute) => (
          <p className="report-attribute" key={attribute.id}>
            <strong>
              {attribute.id}: {attribute.name || "Attribute absent"}
            </strong>
            <br />
            Raw number: {attribute.rawNumeric ?? "Unknown"}; raw text:{" "}
            {attribute.rawText ?? "Unknown"}.<br />
            Normalized: {attribute.normalizedValue ?? "Unknown"}; worst:{" "}
            {attribute.worst ?? "Unknown"}; threshold:{" "}
            {attribute.threshold ?? "Unknown"}; reported failure:{" "}
            {attribute.whenFailed || "not reported"}.
          </p>
        ))}
      </details>
    </>
  );
}
export function SmartReportImport({
  disk,
  onImported,
  initiallyOpen = false,
  onClose,
}: {
  disk: { id: string; serial: string; model: string };
  onImported: () => Promise<void>;
  initiallyOpen?: boolean;
  onClose?: () => void;
}) {
  const { time } = useDisplayFormat();
  const [open, setOpen] = useState(initiallyOpen),
    [json, setJson] = useState(""),
    [continuity, setContinuity] = useState("");
  const [ataConfirmed, setAtaConfirmed] = useState(false),
    [serial, setSerial] = useState("");
  const [preview, setPreview] = useState<Preview | null>(null),
    [history, setHistory] = useState<Saved[]>([]);
  const [busy, setBusy] = useState(false),
    [error, setError] = useState(""),
    [historyError, setHistoryError] = useState("");
  const [message, setMessage] = useState("");
  const previewHeading = useRef<HTMLHeadingElement>(null);
  useEffect(() => { if (preview) previewHeading.current?.focus(); }, [preview]);
  const [historyPage, setHistoryPage] = useState<{ asOf: string; nextOffset: number | null } | null>(null);
  const [historyBusy, setHistoryBusy] = useState(false);
  const historyRequest = useRef<AbortController | null>(null);
  async function loadHistory(older = false) {
    historyRequest.current?.abort();
    const controller = new AbortController();
    historyRequest.current = controller;
    const timer = setTimeout(() => controller.abort(), 15000);
    setHistoryBusy(true);
    setHistoryError("");
    try {
      const query = older && historyPage ? `?asOf=${encodeURIComponent(historyPage.asOf)}&offset=${historyPage.nextOffset}` : "";
      const response = await fetch(`/api/v1/disks/${disk.id}/smart-reports/history${query}`, { signal: controller.signal });
      if (!response.ok) throw new Error();
      const page = await response.json();
      if (!Array.isArray(page.items) || typeof page.asOf !== "string" ||
          !(page.nextOffset === null || Number.isInteger(page.nextOffset) && page.nextOffset > 0)) throw new Error();
      if (controller.signal.aborted) return;
      setHistory(page.items);
      setHistoryPage({ asOf: page.asOf, nextOffset: page.nextOffset });
    } catch {
      if (historyRequest.current === controller)
        setHistoryError("Saved report history could not be loaded. Any displayed reports are retained; try again.");
    } finally {
      clearTimeout(timer);
      if (historyRequest.current === controller) setHistoryBusy(false);
    }
  }
  useEffect(() => {
    setHistory([]);
    setHistoryPage(null);
    setHistoryError("");
    if (open) void loadHistory();
    return () => {
      historyRequest.current?.abort();
      historyRequest.current = null;
    };
  }, [open, disk.id]);
  function edit() {
    setPreview(null);
    setSerial("");
    setError("");
  }
  async function submit(commit: boolean) {
    setBusy(true);
    setError("");
    if (new TextEncoder().encode(json).byteLength > 1024 * 1024) {
      setError("Provide a report no larger than 1 MiB.");
      setBusy(false);
      return;
    }
    const controller = new AbortController();
    const timer = setTimeout(() => controller.abort(), 15000);
    try {
      const response = await fetch(
        `/api/v1/disks/${disk.id}/smart-reports${commit ? "" : "/preview"}`,
        {
          method: "POST",
          signal: controller.signal,
          headers: {
            "Content-Type": "application/json",
            "X-Homelab-Request": "1",
          },
          body: JSON.stringify({
            json,
            continuityGroup: continuity,
            confirmAtaCounterUnits: ataConfirmed,
            expectedHash: preview?.reportHash,
            confirmedSerial: serial,
          }),
        },
      );
      const result = await response.json();
      if (!response.ok)
        throw new Error(result.error || "Report request failed.");
      if (commit) {
        await onImported();
        setMessage(
          `Imported ${result.count} readings. Original source files are not stored; keep your report.`,
        );
        setOpen(false);
      } else setPreview(result);
    } catch (e) {
      setError(
        e instanceof Error && e.name !== "AbortError"
          ? e.message
          : "The request did not finish. Check saved report history before retrying an import.",
      );
    } finally {
      clearTimeout(timer);
      setBusy(false);
    }
  }
  return (
    <div style={{ margin: "20px 0" }}>
      {!initiallyOpen && <Button
        variant="outline"
        onClick={() => {
          setJson("");
          setContinuity("");
          setAtaConfirmed(false);
          edit();
          setMessage("");
          setOpen(true);
        }}
      >
        Import existing SMART report
      </Button>}
      {message && (
        <p role="status" className="muted">
          {message}
        </p>
      )}
      <Dialog
        open={open}
        onOpenChange={(value) => {
          if (!busy) { setOpen(value); if (!value) onClose?.(); }
        }}
      >
        <DialogContent
          className="smart-report-dialog"
          style={{
            width: "min(800px, calc(100vw - 2rem))",
            maxHeight: "85vh",
            overflowY: "auto",
          }}
        >
          <DialogHeader>
            <DialogTitle>Import an existing drive report</DialogTitle>
            <DialogDescription>
              Preview a smartctl JSON report you already have. This app does not
              run smartctl or start any NAS command. Only supported readings and
              curated source details are saved.
            </DialogDescription>
          </DialogHeader>
          <p>
            Selected drive: <strong>{disk.model}</strong> · Serial{" "}
            <strong>{disk.serial || "Unknown"}</strong>
          </p>
          <div className="field">
            <Label htmlFor={`report-file-${disk.id}`}>
              Existing JSON file (up to 1 MiB)
            </Label>
            <Input
              id={`report-file-${disk.id}`}
              type="file"
              accept=".json,application/json"
              disabled={busy}
              onChange={async (event) => {
                const file = event.target.files?.[0];
                if (!file) return;
                edit();
                if (file.size > 1024 * 1024) {
                  setJson("");
                  setError("Choose a report no larger than 1 MiB.");
                  return;
                }
                setBusy(true);
                try {
                  setJson(await file.text());
                } catch {
                  setJson("");
                  setError("Could not read this file.");
                } finally {
                  setBusy(false);
                }
              }}
            />
          </div>
          <div className="field">
            <Label htmlFor={`report-json-${disk.id}`}>Report JSON</Label>
            <Textarea
              id={`report-json-${disk.id}`}
              rows={6}
              disabled={busy}
              value={json}
              onChange={(event) => {
                setJson(event.target.value);
                edit();
              }}
            />
          </div>
          <div className="field">
            <Label htmlFor={`report-epoch-${disk.id}`}>
              Counter continuity group (optional)
            </Label>
            <Input
              id={`report-epoch-${disk.id}`}
              maxLength={200}
              disabled={busy}
              value={continuity}
              onChange={(event) => {
                setContinuity(event.target.value);
                edit();
              }}
            />
            <small>
              Leave blank unless the same counter baseline is established.
              Change it after resets or source changes.
            </small>
          </div>
          <label className="backup-checkbox">
            <input
              type="checkbox"
              disabled={busy}
              checked={ataConfirmed}
              onChange={(event) => {
                setAtaConfirmed(event.target.checked);
                edit();
              }}
            />{" "}
            ATA reports: I verified the selected raw fields are sector/error
            counts for this drive model.
          </label>
          <p className="muted">
            Without that check, ATA raw counters remain unverified. Normalized
            values and vendor thresholds are retained separately and never
            substituted for raw counts.
          </p>
          <Button
            disabled={busy || !json.trim()}
            onClick={() => void submit(false)}
          >
            Preview report
          </Button>
          {error && (
            <p role="alert" className="error">
              {error}
            </p>
          )}
          {preview && (
            <section aria-label="Report preview" className="report-preview">
              <h3 ref={previewHeading} tabIndex={-1}>Review the supported evidence</h3>
              <ReportDetails report={preview} />
              <div className="field" style={{ marginTop: 16 }}>
                <Label htmlFor={`report-confirm-${disk.id}`}>
                  Retype selected drive serial
                </Label>
                <Input
                  id={`report-confirm-${disk.id}`}
                  disabled={busy}
                  value={serial}
                  onChange={(event) => setSerial(event.target.value)}
                  autoComplete="off"
                />
              </div>
              <Button
                disabled={busy || serial !== preview.serial}
                onClick={() => void submit(true)}
              >
                Import reviewed report
              </Button>
            </section>
          )}
          <details>
            <summary>Saved report history</summary>
            <p className="muted">Up to 50 reports per page, ordered by import time. Report observation times remain separate.</p>
            <Button variant="outline" disabled={historyBusy} onClick={() => void loadHistory()}>Refresh report history</Button>
            {historyBusy && <p role="status">Loading saved reports…</p>}
            {historyError && <p role="alert">{historyError}</p>}
            {!history.length && !historyError && !historyBusy && (
              <p className="muted">
                No reports loaded. Earlier single-reading imports appear in
                drive observations.
              </p>
            )}
            {history.map((saved) => (
              <details className="saved-report" key={saved.id}>
                <summary>
                  Imported by {saved.actor} · {time(saved.importedUtc)}
                </summary>
                <ReportDetails report={saved.report} />
              </details>
            ))}
            {historyPage?.nextOffset !== null && historyPage && <Button variant="outline" disabled={historyBusy} onClick={() => void loadHistory(true)}>Older reports</Button>}
            {historyPage && historyPage.nextOffset === null && history.length > 0 && <p className="muted">End of saved report history.</p>}

          </details>
        </DialogContent>
      </Dialog>
    </div>
  );
}
