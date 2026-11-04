import {useDisplayFormat} from "../../../lib/displayPreferences";
import {lazy,Suspense,useEffect,useState} from "react";
import {Card} from "@/components/ui/card";
import {Badge} from "@/components/ui/badge";
import {Button} from "@/components/ui/button";

const VdevEvidenceView = lazy(() => import("./VdevEvidenceView"));

type Summary = {
  id: number;
  dayUtc: string;
  status: string;
  statusCode?: string | null;
  vdevEvidenceJson?: string | null;
  sourceSchema: string;
  scanFunction: string | null;
  scanState: string | null;
  sampleCount: number;
  firstFetchedUtc: string;
  lastFetchedUtc: string;
  freeBytesMin: number | null;
  freeBytesMax: number | null;
  freeBytesKnownCount: number;
  allocatedBytesMin: number | null;
  allocatedBytesMax: number | null;
  allocatedBytesKnownCount: number;
  sizeBytesMin: number | null;
  sizeBytesMax: number | null;
  sizeBytesKnownCount: number;
  scanErrorsMax: number | null;
  scanErrorsKnownCount: number;
  scanPercentageMin: number | null;
  scanPercentageMax: number | null;
  scanPercentageKnownCount: number;
};
type Page = {
  items: Summary[];
  nextDay: string | null;
  nextId: number | null;
  detailDays: number;
  summaryYears: number;
  backupConfigured: boolean;
  pendingSnapshots: number;
};

export function PoolDailyHistoryView({
  connectionId,
  guid,
}: {
  connectionId: string;
  guid: string;
}) {
  const { bytes, time } = useDisplayFormat();
  const range = (min: number | null, max: number | null) =>
    min === max ? bytes(min) : `${bytes(min)} – ${bytes(max)}`;
  const [expanded, setExpanded] = useState(false);
  const [page, setPage] = useState<Page | null>(null);
  const [cursor, setCursor] = useState("");
  const [error, setError] = useState("");
  const [loading, setLoading] = useState(false);
  const [retry, setRetry] = useState(0);
  useEffect(() => {
    if (!expanded) return;
    const controller = new AbortController();
    const timeout = setTimeout(() => controller.abort("timeout"), 15000);
    setLoading(true);
    setError("");
    setPage(null);
    void fetch(
      `/api/v1/connections/${connectionId}/pools/${encodeURIComponent(guid)}/daily-history${cursor}`,
      { signal: controller.signal },
    )
      .then(async (response) => {
        if (!response.ok) throw new Error("Could not load daily summaries.");
        const data: Page = await response.json();
        if (!Array.isArray(data.items))
          throw new Error("Invalid history response.");
        if (!controller.signal.aborted) setPage(data);
      })
      .catch(() => {
        if (
          !controller.signal.aborted ||
          controller.signal.reason === "timeout"
        )
          setError("Daily summaries are unavailable. Try again shortly.");
      })
      .finally(() => {
        clearTimeout(timeout);
        if (
          !controller.signal.aborted ||
          controller.signal.reason === "timeout"
        )
          setLoading(false);
      });
    return () => {
      clearTimeout(timeout);
      controller.abort();
    };
  }, [connectionId, guid, expanded, cursor, retry]);
  return (
    <div style={{ marginTop: 16 }}>
      <Button
        variant="outline"
        aria-expanded={expanded}
        onClick={() => {
          setExpanded(!expanded);
          setCursor("");
        }}
      >
        {expanded ? "Hide daily summaries" : "View older daily summaries"}
      </Button>
      {expanded && (
        <section
          aria-label="Daily pool summaries"
          aria-busy={loading}
          style={{ marginTop: 16 }}
        >
          {loading && <p className="muted">Loading daily summaries…</p>}
          {error && (
            <p role="alert" className="error">
              {error}{" "}
              <Button variant="outline" onClick={() => setRetry(retry + 1)}>
                Retry summaries
              </Button>
            </p>
          )}
          {page && (
            <>
              <p className="muted">
                Detailed snapshots are kept for {page.detailDays} days; older
                daily summaries for {page.summaryYears} years. Days use UTC.
                Each summary groups one pool state, integrity status code, scan state, and source
                schema. Counts are readings, not separate scans or failures.
              </p>
              {!page.backupConfigured && (
                <p role="status">
                  History cleanup is paused until a server backup destination is
                  configured. Existing readings are preserved.
                </p>
              )}
              {page.pendingSnapshots > 0 && (
                <p className="muted">
                  {page.pendingSnapshots.toLocaleString()} older snapshots await
                  cleanup. Cleanup checks hourly and requires a successful
                  backup before removing details.
                </p>
              )}
              {!page.items.length ? (
                <p className="muted">
                  No daily summaries on this page. Recent readings remain in
                  detailed history.
                </p>
              ) : (
                <div aria-label="Daily pool evidence">
                  {page.items.map((row) => (
                    <Card key={row.id} className="daily-summary">
                      <div className="daily-summary-heading">
                        <h4>
                          {row.dayUtc.slice(0, 10)} <small>UTC</small>
                        </h4>
                        <Badge
                          variant={
                            ["DEGRADED", "FAULTED", "UNAVAIL", "SUSPENDED", "OFFLINE"].includes(
                              row.status,
                            )
                              ? "destructive"
                              : "outline"
                          }
                        >
                          {row.status}
                        </Badge>
                      </div>
                      <p>Status code: <strong>{row.statusCode || "Unknown"}</strong></p>
                      {["CORRUPT_DATA", "CORRUPT_POOL"].includes(row.statusCode || "") && (
                        <p className="error">Historical corruption report. Later ONLINE/OK readings do not establish recovery; review the associated finding.</p>
                      )}
                      <p className="muted">
                        {row.sampleCount.toLocaleString()} readings ·{" "}
                        {row.scanFunction || "Unknown scan"} ·{" "}
                        {row.scanState || "Unknown state"}
                      </p>
                      <dl className="daily-summary-metrics">
                        <div>
                          <dt>Free space range</dt>
                          <dd>
                            {range(row.freeBytesMin, row.freeBytesMax)}
                            <small>
                              {row.sampleCount - row.freeBytesKnownCount}{" "}
                              unknown readings
                            </small>
                          </dd>
                        </div>
                        <div>
                          <dt>Allocated range</dt>
                          <dd>
                            {range(
                              row.allocatedBytesMin,
                              row.allocatedBytesMax,
                            )}

                            <small>
                              {row.sampleCount - row.allocatedBytesKnownCount}{" "}
                              unknown readings
                            </small>
                          </dd>
                        </div>
                        <div>
                          <dt>Scan error peak</dt>
                          <dd>
                            {row.scanErrorsMax ?? "Unknown"}
                            <small>
                              {row.sampleCount - row.scanErrorsKnownCount}{" "}
                              unknown readings
                            </small>
                          </dd>
                        </div>
                      </dl>
                      <Suspense fallback={<p>Loading member evidence…</p>}><VdevEvidenceView entries={[{id: String(row.id), label: row.dayUtc.slice(0,10), json: row.vdevEvidenceJson}]} /></Suspense>
                      <details>
                        <summary>Reading details</summary>
                        <p>
                          Size range:{" "}
                          {range(row.sizeBytesMin, row.sizeBytesMax)} (
                          {row.sizeBytesKnownCount} known).
                        </p>
                        <p>
                          Scan progress range:{" "}
                          {row.scanPercentageMin === null
                            ? "Unknown"
                            : `${row.scanPercentageMin}% – ${row.scanPercentageMax}%`}{" "}
                          ({row.scanPercentageKnownCount} known).
                        </p>
                        <p>
                          First read:{" "}
                          {time(row.firstFetchedUtc)}
                          <br />
                          Last read:{" "}
                          {time(row.lastFetchedUtc)}
                        </p>
                        <p>
                          Source: {row.sourceSchema}. Daily ranges do not
                          establish growth rates or scan completion times.
                        </p>
                      </details>
                    </Card>
                  ))}
                </div>
              )}
              <div style={{ display: "flex", gap: 8, marginTop: 12 }}>
                {cursor && (
                  <Button variant="outline" onClick={() => setCursor("")}>
                    Newest summaries
                  </Button>
                )}
                {page.nextDay && page.nextId !== null && (
                  <Button
                    variant="outline"
                    onClick={() =>
                      setCursor(
                        `?beforeDay=${encodeURIComponent(page.nextDay!)}&beforeId=${page.nextId}`,
                      )
                    }
                  >
                    Older daily summaries
                  </Button>
                )}
              </div>
            </>
          )}
        </section>
      )}
    </div>
  );
}
