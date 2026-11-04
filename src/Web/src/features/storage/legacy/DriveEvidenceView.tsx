import {useEffect,useState} from "react";
import {Badge} from "@/components/ui/badge";
import {Button} from "@/components/ui/button";
import {Card} from "@/components/ui/card";
import {useDisplayFormat} from "../../../lib/displayPreferences";
type Reading = {
  id: number;
  value: number | null;
  unit: string;
  observedUtc: string | null;
  receivedUtc: string;
  source: string;
  sourceSchema: string;
  quality: string;
  missingReason: string | null;
  recordedBy: string | null;
};
type Coverage = {
  evaluatedUtc: string;
  staleAfterMinutes: number;
  items: {
    metric: string;
    label: string;
    freshness: string;
    explanation: string;
    latestSubmission: Reading | null;
    latestObserved: Reading | null;
  }[];
};
export function DriveEvidenceView({
  diskId,
  revision,
}: {
  diskId: string;
  revision: number | null;
}) {
  const { time } = useDisplayFormat();
  const [result, setResult] = useState<{ diskId: string; data: Coverage } | null>(null);
  const [failure, setFailure] = useState<{ diskId: string; message: string } | null>(null);
  const [showMissing, setShowMissing] = useState(false);
  const value = result?.diskId === diskId ? result.data : null;
  const error = failure?.diskId === diskId ? failure.message : "";
  const missing = value?.items.filter(item => !item.latestSubmission && !item.latestObserved) ?? [];
  const recorded = value?.items.filter(item => item.latestSubmission || item.latestObserved) ?? [];
  const visible = showMissing ? value?.items ?? [] : recorded;
  const [retry, setRetry] = useState(0);
  useEffect(() => {
    const controller = new AbortController();
    let active = true;
    const timeout = setTimeout(() => controller.abort(), 15000);
    void fetch(`/api/v1/disks/${diskId}/evidence-coverage`, {
      signal: controller.signal,
    })
      .then(async (response) => {
        if (!response.ok) throw new Error();
        const data: Coverage = await response.json();
        if (!data || !Array.isArray(data.items)) throw new Error();
        if (active) {
          setResult({ diskId, data });
          setFailure(null);
        }
      })
      .catch(() => {
        if (active)
          setFailure({ diskId, message:
            "Evidence freshness could not be refreshed. Any displayed assessment is last-known, not current." });
      })
      .finally(() => clearTimeout(timeout));
    return () => {
      active = false;
      controller.abort();
      clearTimeout(timeout);
    };
  }, [diskId, revision, retry]);
  const reading = (r: Reading | null, metric: string) =>
    r?.value == null ? "Unknown" : metric === "smart.overall_failed" ? r.value === 1 ? "Reported failed" : r.value === 0 ? "Reported passed" : "Unknown" : `${r.value.toLocaleString()} ${r.unit}`;
  return (
    <Card
      className="drive-evidence-coverage"
      role="region"
      aria-label="Drive evidence freshness"
    >
      <h2>Drive evidence freshness</h2>
      <p className="muted">
        Imported measurements do not establish automatic SMART coverage or
        complete drive health. Protocol-specific metrics may not apply to this
        drive.
      </p>
      {error && (
        <p role="alert">
          {error}{" "}
          <Button variant="outline" onClick={() => setRetry((v) => v + 1)}>
            Retry evidence freshness
          </Button>
        </p>
      )}
      {!value && !error && <p role="status">Checking recorded evidence…</p>}
      {value && (
        <>
          <p className="muted">
            Assessed {time(value.evaluatedUtc)}. Measurements become stale after{" "}
            {value.staleAfterMinutes.toLocaleString()} minutes from observation
            time. Uploading an old report does not renew it.
          </p>
          <div className="evidence-coverage-summary">
            <p><strong>{recorded.length} metrics with submitted evidence</strong> · {missing.length} without submissions</p>
            <p className="muted">Coverage counts are not a health score. Submitted evidence may still be unknown, unverified or stale.</p>
            {!recorded.length && <p>No evidence has been submitted for this drive. Missing coverage does not mean the drive is healthy.</p>}
            {missing.length > 0 && <Button variant="outline" aria-expanded={showMissing}
              onClick={() => setShowMissing(current => !current)}>
              {showMissing ? "Hide metrics without submissions" : `Show ${missing.length} ${missing.length === 1 ? "metric" : "metrics"} without submissions`}
            </Button>}
          </div>
          <div className="evidence-coverage-grid">
            {visible.map((item) => (
              <div className="evidence-coverage-item" key={item.metric}>
                <div className="section-title">
                  <h3>{item.label}</h3>
                  <Badge variant="outline">{item.freshness}</Badge>
                </div>
                <p>
                  <strong>{reading(item.latestObserved, item.metric)}</strong>
                </p>
                <p className="muted">
                  Observed: {time(item.latestObserved?.observedUtc)}
                </p>
                <details>
                  <summary>Evidence & limitations</summary>
                  <p>{item.explanation}</p>
                  {item.latestObserved && (
                    <p>
                      Observed source: {item.latestObserved.source} · Schema:{" "}
                      {item.latestObserved.sourceSchema}
                    </p>
                  )}
                  {item.latestSubmission ? (
                    <>
                      <p>
                        Latest submission: {item.latestSubmission.quality} ·{" "}
                        {reading(item.latestSubmission, item.metric)}
                      </p>
                      <p>
                        Report time: {time(item.latestSubmission.observedUtc)}.
                        Received: {time(item.latestSubmission.receivedUtc)} ·
                        By: {item.latestSubmission.recordedBy || "Unknown"}.
                      </p>
                      <p>
                        Source: {item.latestSubmission.source} · Schema:{" "}
                        {item.latestSubmission.sourceSchema}
                      </p>
                      {item.latestSubmission.missingReason && (
                        <p>{item.latestSubmission.missingReason}</p>
                      )}
                    </>
                  ) : (
                    <p>
                      No readings have been recorded for this metric. Missing
                      evidence is never treated as zero.
                    </p>
                  )}
                </details>
              </div>
            ))}
          </div>
        </>
      )}
    </Card>
  );
}
