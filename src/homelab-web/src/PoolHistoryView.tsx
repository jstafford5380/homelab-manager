import { useDisplayFormat } from "./displayPreferences";
import type { VdevComparison } from "./VdevEvidenceView";
import { PoolScanHistoryView } from "./PoolScanHistoryView";
import { PoolDailyHistoryView } from "./PoolDailyHistoryView";
import { lazy, Suspense, useEffect, useState } from "react";
import { Button } from "@/components/ui/button";

import { ScanTiming, validScanTiming, type ScanTimingFields } from "./ScanTiming";

const VdevEvidenceView = lazy(() => import("./VdevEvidenceView"));

type Snapshot = ScanTimingFields & {
  id: number;
  fetchedUtc: string;
  status: string;
  statusCode?: string | null;
  vdevEvidenceJson?: string | null;
  vdevComparison?: VdevComparison;
  sizeBytes: number | null;
  allocatedBytes: number | null;
  freeBytes: number | null;
  scanFunction: string | null;
  scanState: string | null;
  scanPercentage: number | null;
  scanErrors: number | null;
};
type Page = { items: Snapshot[]; nextBefore: number | null };

export function PoolHistoryView({
  connectionId,
  guid,
  timezone,
}: {
  connectionId: string;
  guid: string;
  timezone: string;
}) {
  const { bytes: capacity } = useDisplayFormat();
  const [result, setResult] = useState<{ key: string; page: Page } | null>(
    null,
  );
  const [before, setBefore] = useState<number | null>(null);
  const [expanded, setExpanded] = useState(false);
  const [failure, setFailure] = useState<{
    key: string;
    message: string;
  } | null>(null);
  const [pending, setPending] = useState<string | null>(null);
  const [retry, setRetry] = useState(0);
  const key = `${connectionId}/${guid}/${before ?? "latest"}`;
  const page = result?.key === key ? result.page : null;
  const error = failure?.key === key ? failure.message : "";
  const loading = pending === key || (!page && !error);
  useEffect(() => {
    let active = true;
    let current: AbortController | null = null;
    const load = async () => {
      current?.abort();
      const controller = new AbortController();
      current = controller;
      let timedOut = false;
      const timeout = setTimeout(() => {
        timedOut = true;
        controller.abort();
      }, 15000);
      setPending(key);
      try {
        const response = await fetch(
          `/api/v1/connections/${connectionId}/pools/${encodeURIComponent(guid)}/history${before === null ? "" : `?before=${before}`}`,
          { signal: controller.signal },
        );
        if (!response.ok)
          throw new Error("Could not load pool history. Try again shortly.");
        const data: Page = await response.json();
        if (
          !data ||
          !Array.isArray(data.items) ||
          data.items.some(
            (row) =>
              !row ||
              !validScanTiming(row) ||
              !Number.isSafeInteger(row.id) ||
              row.id < 1 ||
              typeof row.status !== "string" ||
              !(row.statusCode == null || typeof row.statusCode === "string") ||
              !Number.isFinite(Date.parse(row.fetchedUtc)) ||
              ![
                row.sizeBytes,
                row.allocatedBytes,
                row.freeBytes,
                row.scanPercentage,
                row.scanErrors,
              ].every(
                (n) =>
                  n === null ||
                  (typeof n === "number" && Number.isFinite(n) && n >= 0),
              ) ||
              ![row.scanFunction, row.scanState].every(
                (v) => v === null || typeof v === "string",
              ),
          ) ||
          !(data.nextBefore === null || Number.isSafeInteger(data.nextBefore))
        )
          throw new Error(
            "The pool history response could not be read safely.",
          );
        if (active && current === controller) {
          setResult({ key, page: data });
          setFailure(null);
        }
      } catch (e) {
        if (active && current === controller)
          setFailure({
            key,
            message: timedOut
              ? "Pool history timed out after 15 seconds."
              : e instanceof Error
                ? e.message
                : "Could not load pool history.",
          });
      } finally {
        clearTimeout(timeout);
        if (active && current === controller) setPending(null);
      }
    };
    void load();
    const timer = setInterval(() => void load(), 30000);
    return () => {
      active = false;
      current?.abort();
      clearInterval(timer);
    };
  }, [connectionId, guid, before, key, retry]);
  const latest = page?.items[0];
  return (
    <div className="pool-history" aria-busy={loading}>
      {error && (
        <p role="alert" className="error">
          {error}{" "}
          {page
            ? "Showing the last successfully loaded page."
            : "No evidence is loaded for this page."}{" "}
          <Button
            variant="outline"
            onClick={() => setRetry((value) => value + 1)}
          >
            Retry pool history
          </Button>
        </p>
      )}
      {!page && !error && <p className="muted">Loading capacity history…</p>}
      {page && !latest && (
        <p className="muted">
          No detailed snapshots on this page. Older readings may be available in
          daily summaries.
        </p>
      )}
      {before !== null && (
        <Button variant="outline" onClick={() => setBefore(null)}>
          Back to latest
        </Button>
      )}
      {latest && (
        <>
          <p>Reported pool status: <strong>{latest.status}</strong> · Status code: <strong>{latest.statusCode || "Unknown"}</strong></p>
          <p className="muted">Pool state and integrity status are separate. ONLINE does not prove data integrity.</p>
          <div className="coverage-grid">
            <div>
              <strong>Allocated</strong>
              <p>{capacity(latest.allocatedBytes)}</p>
            </div>
            <div>
              <strong>Pool free space</strong>
              <p>{capacity(latest.freeBytes)}</p>
            </div>
            <div>
              <strong>Pool size</strong>
              <p>{capacity(latest.sizeBytes)}</p>
            </div>
            <div>
              <strong>Reported scan</strong>
              <p>
                {latest.scanFunction || "Unknown"} ·{" "}
                {latest.scanPausedUtc ? "PAUSED (source report)" : latest.scanState || "Unknown"}
              </p>
              <small>
                {latest.scanPercentage === null
                  ? "Progress unknown"
                  : `${latest.scanPercentage.toFixed(1)}%`}{" "}
                · Errors: {latest.scanErrors ?? "unknown"}
              </small>
              <ScanTiming scan={latest} />
            </div>
          </div>
          <p className="muted">
            {before === null ? "Latest saved snapshot" : "Historical snapshot"}:{" "}
            {new Date(latest.fetchedUtc).toLocaleString(undefined, {
              timeZone: timezone,
            })}
            . Pool space is not dataset usable capacity. Scans are observed
            only.
          </p>
          <Button
            variant="outline"
            aria-expanded={expanded}
            onClick={() => {
              setExpanded(!expanded);
              if (expanded) setBefore(null);
            }}
          >
            {expanded ? "Hide history" : "View capacity & scan history"}
          </Button>
          {expanded && (
            <div style={{ marginTop: 16 }}>
              <p className="muted">
                Fetch times below show when this app read the NAS. Repeated scan
                states do not represent separate scans.
              </p>
              <div
                style={{ overflowX: "auto" }}
                tabIndex={0}
                role="region"
                aria-label="Pool snapshot history"
              >
                <table className="pool-history-table">
                  <caption className="sr-only">
                    Saved pool capacity and scan snapshots, newest first
                  </caption>
                  <thead>
                    <tr>
                      {[
                        "Fetched",
                        "Pool state",
                        "Allocated",
                        "Free",
                        "Scan",
                        "Progress",
                        "Scan errors",
                      ].map((label) => (
                        <th key={label} scope="col">
                          {label}
                        </th>
                      ))}
                    </tr>
                  </thead>
                  <tbody>
                    {page.items.map((row) => (
                      <tr key={row.id}>
                        <td>
                          {new Date(row.fetchedUtc).toLocaleString(undefined, {
                            timeZone: timezone,
                          })}
                        </td>
                        <td>{row.status}<br /><small>Status code: {row.statusCode || "Unknown"}</small></td>
                        <td>{capacity(row.allocatedBytes)}</td>
                        <td>{capacity(row.freeBytes)}</td>
                        <td>
                          {row.scanFunction || "Unknown"} ·{" "}
                          {row.scanPausedUtc ? "PAUSED (source report)" : row.scanState || "Unknown"}
                          <ScanTiming scan={row} />
                        </td>
                        <td>
                          {row.scanPercentage === null
                            ? "Unknown"
                            : `${row.scanPercentage.toFixed(1)}%`}
                        </td>
                        <td>{row.scanErrors ?? "Unknown"}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
              <div style={{ display: "flex", gap: 8, marginTop: 12 }}>
                {page.nextBefore !== null && (
                  <Button
                    variant="outline"
                    disabled={loading}
                    onClick={() => setBefore(page.nextBefore)}
                  >
                    Older snapshots
                  </Button>
                )}
              </div>
            </div>
          )}
        </>
      )}
      {page && <Suspense fallback={<p>Loading member evidence…</p>}><VdevEvidenceView key={key} entries={page.items.map(row => ({id: String(row.id), label: new Date(row.fetchedUtc).toLocaleString(undefined, {timeZone: timezone}), json: row.vdevEvidenceJson, comparison: row.vdevComparison}))} /></Suspense>}
      <PoolScanHistoryView connectionId={connectionId} guid={guid} />
      <PoolDailyHistoryView connectionId={connectionId} guid={guid} />
    </div>
  );
}
