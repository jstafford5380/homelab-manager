import {SelectField,SelectOption} from "@/components/SelectField";
import {useDisplayFormat} from "../../../lib/displayPreferences";
import {useEffect,useState} from "react";
import {Button} from "@/components/ui/button";
import {Badge} from "@/components/ui/badge";
type Estimate = {
  status: string;
  explanation: string;
  samples: number;
  days: number;
  firstUtc: string | null;
  lastUtc: string | null;
  sizeBytes: number | null;
  allocatedBytes: number | null;
  growthBytesPerDay: number | null;
  daysTo80Percent: number | null;
  fit: number | null;
};
const statuses: Record<string, string> = {
  paused: "Collection paused",
  estimated: "Planning estimate",
  insufficient: "Building history",
  stale: "Stale evidence",
  suppressed: "Estimate withheld",
  variable: "Variable growth",
  "not-growing": "No measured growth",
  distant: "Beyond useful horizon",
  "threshold-reached": "Planning marker reached",
};
export function PoolCapacityView({
  connectionId,
  guid,
  timezone,
}: {
  connectionId: string;
  guid: string;
  timezone: string;
}) {
  const [days, setDays] = useState(30),
    [revision, setRevision] = useState(0);
  const [value, setValue] = useState<Estimate | null>(null),
    [error, setError] = useState("");
  useEffect(() => {
    const controller = new AbortController();
    let active = true;
    setValue(null);
    setError("");
    const timeout = setTimeout(() => controller.abort(), 15000);
    void fetch(
      `/api/v1/connections/${connectionId}/pools/${encodeURIComponent(guid)}/capacity?days=${days}`,
      { signal: controller.signal },
    )
      .then(async (response) => {
        if (!response.ok) throw new Error();
        const result: Estimate = await response.json();
        if (active) setValue(result);
      })
      .catch(() => {
        if (active)
          setError("Capacity assessment could not be loaded. Try again.");
      })
      .finally(() => clearTimeout(timeout));
    const refresh = setInterval(() => setRevision((r) => r + 1), 60000);
    return () => {
      active = false;
      controller.abort();
      clearTimeout(timeout);
      clearInterval(refresh);
    };
  }, [connectionId, guid, days, revision]);
  const { bytes } = useDisplayFormat();
  return (
    <section className="capacity-planning" aria-label="Pool capacity planning">
      <div className="section-title">
        <h4>Capacity planning</h4>
        <label>
          Growth window{" "}
          <SelectField
            aria-label="Capacity growth window"
            value={days}
            onValueChange={(e) => setDays(Number(e))}
          >
            {[7, 30, 90].map((d) => (
              <SelectOption key={d} value={d}>
                {d} days
              </SelectOption>
            ))}
          </SelectField>
        </label>
      </div>
      {error ? (
        <p role="alert">
          {error}{" "}
          <Button variant="outline" onClick={() => setRevision((r) => r + 1)}>
            Retry
          </Button>
        </p>
      ) : !value ? (
        <p role="status">Assessing capacity history…</p>
      ) : (
        <>
          <Badge variant="outline">{statuses[value.status] || "Unknown"}</Badge>
          <div className="coverage-grid">
            <div>
              <strong>Latest allocation</strong>
              <p>
                {bytes(value.allocatedBytes)} / {bytes(value.sizeBytes)}
              </p>
            </div>
            <div>
              <strong>Measured daily growth</strong>
              <p>
                {value.growthBytesPerDay === null
                  ? "Not enough stable evidence"
                  : `${bytes(value.growthBytesPerDay)} / day`}
              </p>
            </div>
            <div>
              <strong>Time to 80% allocated</strong>
              <p>
                {value.daysTo80Percent === null
                  ? "No estimate"
                  : value.daysTo80Percent === 0
                    ? "Marker already reached"
                    : `About ${Math.max(1, Math.round(value.daysTo80Percent))} days`}
              </p>
            </div>
          </div>
          <p>{value.explanation}</p>
          <details>
            <summary>Evidence and assumptions</summary>
            <p>
              {value.samples.toLocaleString()} readings across {value.days} UTC
              days. Trend uses the last reading of each day, so frequent polls
              do not dominate the estimate.
            </p>
            <p>
              Window:{" "}
              {value.firstUtc
                ? new Date(value.firstUtc).toLocaleString(undefined, {
                    timeZone: timezone,
                  })
                : "Unknown"}{" "}
              –{" "}
              {value.lastUtc
                ? new Date(value.lastUtc).toLocaleString(undefined, {
                    timeZone: timezone,
                  })
                : "Unknown"}
              .
            </p>
            <p>
              Linear fit:{" "}
              {value.fit === null ? "Unavailable" : value.fit.toFixed(2)}. This
              is a trend-fit measure, not a probability or a confidence
              interval. Estimates assume unchanged capacity and workload; they
              are not failure predictions.
            </p>
          </details>
        </>
      )}
    </section>
  );
}
