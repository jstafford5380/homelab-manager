import {capabilityLabel} from "../../../lib/capabilityLabels";
import {useEffect,useState} from "react";
import {Button} from "@/components/ui/button";
import {Card} from "@/components/ui/card";
import {Badge} from "@/components/ui/badge";

type Coverage = {
  known: boolean;
  items: { name: string; status: string; expectedSeconds: number | null }[];
};
type Attempt = {
  id: number;
  startedUtc: string;
  result: string;
  durationMs: number | null;
  coverage: Coverage;
};
type Daily = {
  id: number;
  dayUtc: string;
  result: string;
  attemptCount: number;
  firstStartedUtc: string;
  lastStartedUtc: string;
  durationKnownCount: number;
  durationMinMs: number | null;
  durationMaxMs: number | null;
  coverage: Coverage;
};
type Page = {
  items: (Attempt | Daily)[];
  nextId: number | null;
  nextDay?: string | null;
  detailDays?: number;
  summaryYears?: number;
  backupConfigured?: boolean;
  pendingAttempts?: number;
};
const duration = (ms: number | null) =>
  ms === null
    ? "Unknown"
    : `${(ms / 1000).toLocaleString(undefined, { maximumFractionDigits: 2 })} s`;

export function CollectionHistoryView({
  connectionId,
  timezone,
}: {
  connectionId: string;
  timezone: string;
}) {
  const [expanded, setExpanded] = useState(false);
  const [daily, setDaily] = useState(false);
  const [cursor, setCursor] = useState("");
  const [page, setPage] = useState<Page | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState("");
  const [revision, setRevision] = useState(0);
  useEffect(() => {
    if (!expanded) return;
    const controller = new AbortController();
    const timeout = setTimeout(() => controller.abort("timeout"), 15000);
    setLoading(true);
    setPage(null);
    setError("");
    void fetch(
      `/api/v1/connections/${connectionId}/${daily ? "collection-daily-history" : "collection-history"}${cursor}`,
      { signal: controller.signal },
    )
      .then(async (response) => {
        if (!response.ok) throw new Error("Unavailable");
        const data: Page = await response.json();
        if (!Array.isArray(data.items)) throw new Error("Invalid response");
        if (!controller.signal.aborted) setPage(data);
      })
      .catch(() => {
        if (
          !controller.signal.aborted ||
          controller.signal.reason === "timeout"
        )
          setError("Collection history is unavailable. Refresh to try again.");
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
  }, [connectionId, daily, cursor, expanded, revision]);
  function changeView(value: boolean) {
    if (value === daily) return;
    setPage(null);
    setDaily(value);
    setCursor("");
  }
  const format = (date: string) =>
    new Date(date).toLocaleString(undefined, { timeZone: timezone });
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
        {expanded ? "Hide collection history" : "View collection history"}
      </Button>
      {expanded && (
        <section
          aria-label="Collection attempt history"
          aria-busy={loading}
          style={{ marginTop: 16 }}
        >
          <p className="muted">
            Saved collection attempts explain monitoring gaps. Counts and
            durations do not measure NAS uptime or drive health. A partial
            attempt can include unsupported or unavailable capabilities.
          </p>
          <div className="review-actions" style={{ marginTop: 12 }}>
            <div
              role="group"
              aria-label="Collection history period"
              style={{ display: "flex", flexWrap: "wrap", gap: 8 }}
            >
              <Button
                variant={daily ? "outline" : "default"}
                aria-pressed={!daily}
                onClick={() => changeView(false)}
              >
                Recent attempts
              </Button>
              <Button
                variant={daily ? "default" : "outline"}
                aria-pressed={daily}
                onClick={() => changeView(true)}
              >
                Daily attempt summaries
              </Button>
            </div>
            <Button variant="ghost" onClick={() => setRevision(revision + 1)}>
              Refresh attempts
            </Button>
          </div>
          {error && (
            <p role="alert" className="error">
              {error}
            </p>
          )}
          {loading && <p className="muted">Loading collection history…</p>}
          {page && (
            <>
              {daily && (
                <p className="muted" style={{ marginTop: 12 }}>
                  Details are kept for {page.detailDays} days; summaries for{" "}
                  {page.summaryYears} years. Summary days use UTC. Separate
                  groups preserve different outcomes, capability states, and
                  polling intervals.
                </p>
              )}
              {daily && page.backupConfigured === false && (
                <p role="status">
                  Collection history cleanup is paused until a server backup
                  destination is configured.
                </p>
              )}
              {daily && (page.pendingAttempts ?? 0) > 0 && (
                <p className="muted">
                  {page.pendingAttempts?.toLocaleString()} older attempts await
                  cleanup and a successful backup.
                </p>
              )}
              {!page.items.length && (
                <p className="muted" style={{ marginTop: 12 }}>
                  No {daily ? "daily summaries" : "attempts"} on this page.
                  Missing history is not evidence of successful monitoring.
                </p>
              )}
              {page.items.map((item) => (
                <Card key={item.id} className="daily-summary">
                  <div className="daily-summary-heading">
                    <h4>
                      {"dayUtc" in item
                        ? `${item.dayUtc.slice(0, 10)} UTC`
                        : format(item.startedUtc)}
                    </h4>
                    <Badge
                      variant={
                        item.result === "denied" ||
                        item.result === "unavailable"
                          ? "destructive"
                          : "outline"
                      }
                    >
                      {item.result || "Unknown outcome"}
                    </Badge>
                  </div>
                  {"attemptCount" in item ? (
                    <>
                      <p>
                        {item.attemptCount.toLocaleString()} attempts ·
                        Duration: {duration(item.durationMinMs)} –{" "}
                        {duration(item.durationMaxMs)}
                      </p>
                      <small>
                        {item.attemptCount - item.durationKnownCount} unknown
                        durations. First attempt: {format(item.firstStartedUtc)}
                        . Last: {format(item.lastStartedUtc)}.
                      </small>
                    </>
                  ) : (
                    <p>Attempt duration: {duration(item.durationMs)}</p>
                  )}
                  <details>
                    <summary>Recorded capability states</summary>
                    {item.coverage.known ? (
                      <ul style={{ marginTop: 12 }}>
                        {item.coverage.items.map((capability) => (
                          <li key={capability.name} style={{ marginBottom: 8 }}>
                            <strong>{capabilityLabel(capability.name)}</strong>:{" "}
                            {capability.status} · expected interval{" "}
                            {capability.expectedSeconds === null
                              ? "unknown"
                              : `${capability.expectedSeconds} s`}
                          </li>
                        ))}
                      </ul>
                    ) : (
                      <p>
                        Capability evidence is unknown or could not be read.
                      </p>
                    )}
                    <p>
                      These are saved states at the time of the attempt. A
                      supported state may reflect an earlier read when a
                      capability was not due for collection.
                    </p>
                  </details>
                </Card>
              ))}
              <div
                style={{
                  display: "flex",
                  flexWrap: "wrap",
                  gap: 8,
                  marginTop: 12,
                }}
              >
                {cursor && (
                  <Button
                    variant="outline"
                    onClick={() => {
                      setPage(null);
                      setCursor("");
                    }}
                  >
                    Newest attempts
                  </Button>
                )}
                {page.nextId !== null && (
                  <Button
                    variant="outline"
                    onClick={() => {
                      setPage(null);
                      setCursor(
                        daily
                          ? `?beforeDay=${encodeURIComponent(page.nextDay!)}&beforeId=${page.nextId}`
                          : `?before=${page.nextId}`,
                      );
                    }}
                  >
                    Older attempts
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
