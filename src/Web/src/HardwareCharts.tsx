import { useState } from "react";
import { Link } from "react-router";
import { Card } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import {
  gb,
  number,
  type HwDetail,
  type HwMetric,
  type HwStatus,
} from "./hardwareModel";
export function HardwarePill({
  status,
  stale = false,
}: {
  status: HwStatus;
  stale?: boolean;
}) {
  return (
    <span className={`hw-pill hw-${stale ? "neutral" : status.tone}`}>
      {stale && !status.label.startsWith("Last known:") ? "Last known: " : ""}
      {status.label}
    </span>
  );
}
export function HardwareChart({
  metric,
  title,
  days,
  total,
  endUtc,
}: {
  metric: HwMetric;
  title: string;
  days: number;
  total?: number | null;
  endUtc?: string | null;
}) {
  const [openedAt] = useState(Date.now);
  const end = endUtc ? new Date(endUtc).getTime() : openedAt,
    start = end - days * 86400000;
  const level =
    metric.reviewLevel == null
      ? null
      : total
        ? (metric.reviewLevel / 100) * total
        : metric.reviewLevel;
  const max =
    Math.max(total || 0, level || 0, ...metric.series.map((p) => p.value), 1) *
    1.05;
  const x = (at: string) =>
    4 + ((new Date(at).getTime() - start) / (end - start)) * 292;
  const y = (value: number) => 66 - (value / max) * 58;
  const line = metric.series.filter((p) => p.source === "collected");
  const gap = Math.max(15 * 60000, ((days * 86400000) / 288) * 2);
  return (
    <svg
      viewBox="0 0 300 76"
      role="img"
      aria-label={`${title}, last ${days === 1 ? "24 hours" : `${days} days`}, ${metric.partial ? "latest sample" : "average"} ${number(metric.average ?? metric.latest)}, peak ${number(metric.peak)}, review level ${number(metric.reviewLevel)}. Entered readings are dots.`}
    >
      <path d="M4 70 H296" className="hw-axis" />
      {level != null && (
        <path d={`M4 ${y(level)} H296`} className="hw-review" />
      )}
      <path
        d={line
          .map(
            (p, i) =>
              `${i === 0 || new Date(p.atUtc).getTime() - new Date(line[i - 1].atUtc).getTime() > gap ? "M" : "L"}${x(p.atUtc)} ${y(p.value)}`,
          )
          .join(" ")}
        className="hw-line"
      />
      {metric.series
        .filter((p) => p.source === "entered")
        .map((p, i) => (
          <circle
            key={p.readingId ?? i}
            cx={x(p.atUtc)}
            cy={y(p.value)}
            r="3.4"
            className="hw-dot"
          >
            <title>
              {number(p.value)} · entered by you ·{" "}
              {new Date(p.atUtc).toLocaleString()}
            </title>
          </circle>
        ))}
    </svg>
  );
}
export function HardwareMetricCard({
  title,
  metric: m,
  detail: d,
  type,
  stale,
  onReading,
}: {
  title: string;
  metric: HwMetric;
  detail: HwDetail;
  type: "cpu" | "memory" | "temperature" | "power";
  stale: boolean;
  onReading: () => void;
}) {
  const days = d.coverage.days,
    range = days === 1 ? "24-hour" : `${days}-day`,
    memory = type === "memory",
    temperature = type === "temperature";
  const format = (v: number | null) =>
    memory
      ? gb(v)
      : number(v, type === "cpu" ? "%" : temperature ? " °C" : " W");
  const value = m.average ?? m.latest,
    cache = d.server.platform === "truenas" ? "ZFS cache" : "File cache";
  const reason =
    type === "power"
      ? `${d.server.platform === "truenas" ? "TrueNAS" : "Synology"} doesn’t report power draw. Add a reading from a power meter or your UPS to track it here.`
      : "No measurements for this period. Add a reading from the server’s own dashboard.";
  return (
    <Card className="hw-metric">
      <div className="hw-card-heading">
        <h3>{title}</h3>
        {m.status && <HardwarePill status={m.status} stale={stale} />}
      </div>
      {m.source === "none" ? (
        <>
          <strong className="hw-unmeasured-title">
            {type === "power"
              ? "Not measured"
              : d.readings.length
                ? "Not in your readings"
                : "No readings"}
          </strong>
          <div className="hw-hatch" aria-hidden="true" />
          <p>{reason}</p>
          <Button variant="link" onClick={onReading}>
            Add a reading
          </Button>
        </>
      ) : (
        <>
          <div>
            <strong className="hw-value">{format(value)}</strong>
            {memory && <small> of {gb(d.totalBytes)} used by apps</small>}
          </div>
          <p
            className={`hw-caption ${m.source === "entered" ? "hw-entered-caption" : ""}`}
          >
            {m.source === "entered"
              ? `From ${m.readingCount} readings · entered by you`
              : m.partial
                ? `Latest sample · not enough history for a ${range} average yet`
                : `${range} average`}
            {stale &&
              ` · last known, up to ${d.server.lastSuccessUtc ? new Date(d.server.lastSuccessUtc).toLocaleDateString() : "unknown"}`}
          </p>
          <HardwareChart
            metric={m}
            title={title}
            days={days}
            total={memory ? d.totalBytes : null}
            endUtc={stale ? d.server.lastSuccessUtc : null}
          />
          {memory &&
            m.cacheAverage != null &&
            m.freeAverage != null &&
            d.totalBytes != null && (
              <>
                <div
                  className="hw-memory-bar"
                  role="img"
                  aria-label={`Apps ${gb(value)}, ${cache} ${gb(m.cacheAverage)}, free ${gb(m.freeAverage)}`}
                >
                  <span
                    style={{ width: `${((value ?? 0) / d.totalBytes) * 100}%` }}
                  />
                  <span
                    style={{
                      width: `${(m.cacheAverage / d.totalBytes) * 100}%`,
                    }}
                  />
                </div>
                <p>
                  Apps {gb(value)} · {cache} {gb(m.cacheAverage)} · Free{" "}
                  {gb(m.freeAverage)}
                </p>
                <p>
                  {d.server.platform === "truenas"
                    ? "ZFS borrows spare memory as a disk cache"
                    : "DSM uses spare memory to cache files"}{" "}
                  and gives it back when apps need it, so it isn’t counted as
                  used.
                </p>
              </>
            )}
          <ul>
            <li>
              {m.source === "entered"
                ? "Highest reading "
                : temperature
                  ? "Hottest "
                  : "Peak "}
              {format(
                type === "cpu" && m.source === "collected" ? m.p95 : m.peak,
              )}
              {type === "cpu" &&
                m.source === "collected" &&
                " (95th percentile)"}
            </li>
            {m.previousAverage != null && (
              <li>
                Previous {days === 1 ? "24 hours" : `${days} days`}:{" "}
                {format(m.previousAverage)}
              </li>
            )}
            {m.reviewLevel != null && (
              <li>
                {m.source === "entered"
                  ? `Above your ${m.reviewLevel}${temperature ? " °C" : "%"} review level in ${m.readingsAboveLevel} of ${m.readingCount} readings`
                  : m.daysAboveLevel
                    ? `Above your ${m.reviewLevel}${temperature ? " °C" : "%"} review level${temperature ? "" : ` for ${d.sustainedMinutes} minutes or more`} on ${m.daysAboveLevel} of the last ${days} days`
                    : `No sampled value${temperature ? "" : " sustained for the review duration"} above your ${m.reviewLevel}${temperature ? " °C" : "%"} review level`}
              </li>
            )}
            {type === "power" && value != null && (
              <>
                <li>
                  Last reading{" "}
                  {m.series.length
                    ? new Date(
                        m.series[m.series.length - 1].atUtc,
                      ).toLocaleDateString()
                    : ""}
                  : {format(m.latest)}
                </li>
                <li>
                  About {Math.round((value * 24 * 30) / 1000)} kWh a month at
                  this rate
                </li>
              </>
            )}
          </ul>
          {m.source === "collected" && m.readingCount > 0 && (
            <p>
              Also shown: {m.readingCount} dots entered by you; excluded from
              collected averages.
            </p>
          )}
        </>
      )}
    </Card>
  );
}
export function HardwareReviewLine({ detail: d }: { detail: HwDetail }) {
  return (
    <p className="hw-review-line">
      Review levels: CPU {d.cpuLevel}%, memory {d.memoryLevel}%, temperature{" "}
      {d.temperatureLevel} °C. Dashed lines on the charts.{" "}
      <Link to="/settings#hardware-review-levels">Change in Settings</Link>
    </p>
  );
}
