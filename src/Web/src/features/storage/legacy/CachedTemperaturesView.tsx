import {useEffect,useState} from "react";
import {Button} from "@/components/ui/button";
import {Badge} from "@/components/ui/badge";
import {Card} from "@/components/ui/card";

type Cache = {
  enabled: boolean;
  status: string;
  lastAttemptUtc: string | null;
  lastSuccessUtc: string | null;
  snapshot: {
    fetchedUtc: string;
    sourceSchema: string;
    readings: {
      deviceName: string;
      celsius: number | null;
      missingReason: string | null;
    }[];
  } | null;
};
export function CachedTemperaturesView({
  connectionId,
  timezone,
}: {
  connectionId: string;
  timezone: string;
}) {
  const [expanded, setExpanded] = useState(false);
  const [cache, setCache] = useState<Cache | null>(null);
  const [error, setError] = useState("");
  const [loading, setLoading] = useState(false);
  const [revision, setRevision] = useState(0);
  useEffect(() => {
    if (!expanded) return;
    const timer = setInterval(() => setRevision((n) => n + 1), 30000);
    return () => clearInterval(timer);
  }, [expanded]);
  useEffect(() => {
    if (!expanded) return;
    const controller = new AbortController();
    const timeout = setTimeout(() => controller.abort("timeout"), 15000);
    setLoading(true);
    void fetch(`/api/v1/connections/${connectionId}/cached-temperatures`, {
      signal: controller.signal,
    })
      .then(async (response) => {
        if (!response.ok) throw new Error("Unavailable");
        const data: Cache = await response.json();
        if (
          typeof data.enabled !== "boolean" ||
          typeof data.status !== "string" ||
          (data.snapshot && !Array.isArray(data.snapshot.readings))
        )
          throw new Error("Invalid response");
        if (!controller.signal.aborted) {
          setCache(data);
          setError("");
        }
      })
      .catch(() => {
        if (
          !controller.signal.aborted ||
          controller.signal.reason === "timeout"
        )
          setError(
            "Could not refresh cached temperatures. Any values below are from the last saved response.",
          );
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
  }, [connectionId, expanded, revision]);
  const format = (time: string) =>
    new Date(time).toLocaleString(undefined, { timeZone: timezone });
  return (
    <div style={{ marginTop: 16 }}>
      <Button
        variant="outline"
        aria-expanded={expanded}
        onClick={() => setExpanded(!expanded)}
      >
        {expanded ? "Hide cached temperatures" : "View cached temperatures"}
      </Button>
      {expanded && (
        <section
          aria-label="Cached drive temperatures"
          aria-busy={loading}
          style={{ marginTop: 16 }}
        >
          <div className="section-title">
            <h3>NAS temperature cache</h3>
            <Button
              variant="ghost"
              onClick={() => setRevision((n) => n + 1)}
              disabled={loading}
            >
              Refresh cache view
            </Button>
          </div>
          <p className="muted">
            Reads existing NAS cache entries only. No fresh drive query or test
            is started. Observation times and physical drive identities are
            unknown, so these values are not used in drive-health trends or
            replacement advice.
          </p>
          {error && (
            <p role="alert" className="error">
              {error}
            </p>
          )}
          {!cache && loading && <p className="muted">Loading cache status…</p>}
          {cache && (
            <>
              <p style={{ marginTop: 12 }}>
                <Badge variant="outline">
                  {cache.status === "supported"
                    ? "Cache API available"
                    : cache.status}
                </Badge>
              </p>
              {!cache.enabled && (
                <p className="muted">
                  Enable “Read existing NAS temperature cache” in Settings to
                  collect these optional readings.
                </p>
              )}
              {cache.status !== "supported" && cache.snapshot && (
                <p className="muted">
                  Showing the last saved cache. A failed or paused read does not
                  confirm current temperatures.
                </p>
              )}
              <p className="muted">
                Last cache read:{" "}
                {cache.lastSuccessUtc ? format(cache.lastSuccessUtc) : "Never"}.
              </p>
              {!cache.snapshot ? (
                <p>No saved temperature cache is available.</p>
              ) : (
                <>
                  <p className="muted">
                    Cache fetched {format(cache.snapshot.fetchedUtc)}. The
                    source filters cache entries to roughly ten minutes old, but
                    does not provide each reading’s time. Device names are
                    labels, not verified physical-disk matches.
                  </p>
                  {!cache.snapshot.readings.length && (
                    <p>
                      The returned cache was empty. Missing temperatures are
                      unknown, not zero or healthy.
                    </p>
                  )}
                  <div className="planning-context" style={{ marginTop: 12 }}>
                    {cache.snapshot.readings.map((reading) => (
                      <Card className="daily-summary" key={reading.deviceName}>
                        <h4>{reading.deviceName}</h4>
                        <p style={{ fontSize: "1.2rem", fontWeight: 600 }}>
                          {reading.celsius === null
                            ? "Unknown temperature"
                            : `${reading.celsius} °C`}
                        </p>
                        {reading.missingReason && (
                          <small>{reading.missingReason}</small>
                        )}
                        <small>Physical drive match unverified</small>
                      </Card>
                    ))}
                  </div>
                  <details style={{ marginTop: 16 }}>
                    <summary>Source details</summary>
                    <p className="muted">
                      {cache.snapshot.sourceSchema}. Only the latest bounded
                      snapshot is saved. Full SMART attribute monitoring remains
                      unsupported.
                    </p>
                  </details>
                </>
              )}
            </>
          )}
        </section>
      )}
    </div>
  );
}
