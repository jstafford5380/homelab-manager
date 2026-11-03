import { Info, Pencil } from "lucide-react";
import { Tooltip } from "radix-ui";
import { useState, type ReactNode } from "react";
import { Link } from "react-router";
import { Card } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import {
  gb,
  estimateUpsWatts,
  type HwUpsDevice,
  type HwUpsMonthlyPower,
  type HwComponent,
  number,
  type HwDetail,
  type HwMetric,
  type HwStatus,
  type HwUps,
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
function EstimateInfo({ label, children }: { label: string; children: ReactNode }) {
  return <Tooltip.Provider delayDuration={150}>
    <Tooltip.Root>
      <Tooltip.Trigger asChild>
        <Button type="button" variant="ghost" size="icon" className="hw-estimate-info" aria-label={label}>
          <Info aria-hidden="true" size={15} />
        </Button>
      </Tooltip.Trigger>
      <Tooltip.Portal>
        <Tooltip.Content className="hw-estimate-tooltip" sideOffset={6}>{children}</Tooltip.Content>
      </Tooltip.Portal>
    </Tooltip.Root>
  </Tooltip.Provider>;
}

function UpsReadings({ ups, stale, device, component, onSetup, watts = null, monthly, electricityRate }: {
  ups: HwUps; stale: boolean; device?: HwUpsDevice; component?: HwComponent;
  onSetup?: (key: string) => void; watts?: number | null;
  monthly?: HwUpsMonthlyPower; electricityRate?: number | null;
}) {
  const factor = component?.record.upsPowerFactor;
  const enteredVa = component?.record.upsNominalVa;
  const rating = device ? device.nominalVa ?? enteredVa : ups.nominalVa;
  const runtime = (seconds: number) => {
    const minutes = Math.round(seconds / 60);
    if (minutes < 1) return "Under 1 min";
    const hours = Math.floor(minutes / 60);
    return hours ? `About ${hours} h ${minutes % 60} min` : `About ${minutes} min`;
  };
  const rows = [
    { label: "Current load", reading: device ? device.load : ups.load, format: (value: number) => number(value, "%") },
    { label: "Battery charge", reading: device ? device.charge : ups.charge, format: (value: number) => number(value, "%") },
    { label: "Estimated runtime", reading: device ? device.runtime : ups.runtime, format: runtime },
  ];
  return <section className="hw-ups" aria-label={device ? `UPS readings for ${component?.title ?? device.name}` : "UPS readings"}>
    <h4>{device ? component?.title ?? device.model ?? device.name : "UPS readings"}</h4>
    {!device && ups.status !== "reporting" && <p className="hw-caption">
      {ups.status === "denied" ? "TrueNAS denied access to UPS reporting."
        : ups.status === "unsupported" ? "UPS reporting is unavailable through this TrueNAS connection."
        : ups.status === "unavailable" ? "The UPS reporting request failed."
        : "TrueNAS returned no usable UPS readings."}
    </p>}
    <dl>
      {rows.map(({ label, reading, format }) => <div key={label}>
        <dt>{label}</dt>
        <dd>{reading ? format(reading.value) : "Unknown"}
          {reading && (stale || Date.now() - Date.parse(reading.observedUtc) > 15 * 60_000) &&
            <span className="hw-caption"> · Last known</span>}
        </dd>
      </div>)}
      <div><dt>Nominal rating</dt><dd>{rating == null ? "Unavailable" : number(rating, " VA")}{device && device.nominalVa == null && enteredVa != null && " · entered"}</dd></div>
    </dl>
    {device && !device.key && <p className="hw-caption">The UPS did not provide a distinct identity for linking.</p>}
    {!component && device?.key && !stale && <Button variant="link" onClick={() => onSetup?.(device.key!)}>Set up this UPS</Button>}
    {watts != null && <div className="hw-ups-estimate">
      <div className="hw-estimate-heading">
        <strong className="hw-value">≈{number(Math.round(watts), " W")} estimated</strong>
        <EstimateInfo label="How estimated wattage is calculated">
          {number(rating, " VA")} × {number(device?.load?.value, "%")} × {factor}
        </EstimateInfo>
      </div>
    </div>}
    {device && factor != null && <div className="hw-ups-estimate">
      <div className="hw-estimate-heading">
        <h4>Estimated monthly cost</h4>
        {monthly && <EstimateInfo label="How monthly cost is calculated">
          {number(monthly.medianWatts, " W")} median over the last 30 days · {monthly.sampleCount.toLocaleString()} {monthly.sampleCount === 1 ? "sample" : "samples"} across {monthly.daysWithSamples} UTC {monthly.daysWithSamples === 1 ? "day" : "days"}.
          {monthly.estimatedCostUsd != null && <> Projects the median draw across 720 hours at {electricityRate?.toLocaleString(undefined, { maximumFractionDigits: 6 })} USD/kWh.</>}
        </EstimateInfo>}
      </div>
      {monthly?.estimatedCostUsd != null && <strong>{monthly.estimatedCostUsd.toLocaleString(undefined, { style: "currency", currency: "USD" })} per 30 days</strong>}
      {monthly && monthly.daysWithSamples < 30 && monthly.estimatedCostUsd != null && <p className="hw-caption">Preliminary estimate</p>}
      {!monthly && <p className="hw-caption">Waiting for usable power samples.</p>}
      {electricityRate == null && <Link to="/settings/other">Set electricity rate</Link>}
    </div>}
  </section>;
}
export function HardwareMetricCard({
  title,
  metric: m,
  detail: d,
  type,
  stale,
  onReading,
  onSetupUps,
  onEditUps,
}: {
  title: string;
  metric: HwMetric;
  detail: HwDetail;
  type: "cpu" | "memory" | "temperature" | "power";
  stale: boolean;
  onReading: () => void;
  onSetupUps?: (key: string) => void;
  onEditUps?: (id: string) => void;
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
  const ups = type === "power" ? d.ups : null;
  const upsDevices = ups?.devices?.map(device => {
    const component = device.key ? d.components.find(part => part.record.upsDeviceKey === device.key) : undefined;
    const watts = stale ? null : estimateUpsWatts(device, component?.record.upsPowerFactor, component?.record.upsNominalVa);
    return { device, component, watts };
  });
  const integratedUps = !!upsDevices?.length || ups?.status === "reporting";
  const hasEstimate = upsDevices?.some(({ watts }) => watts != null);
  const reason =
    type === "power"
      ? `${d.server.platform === "truenas" ? "TrueNAS" : "Synology"} doesn’t report measured wattage here. Add a wattage reading from a power meter or your UPS.`
      : "No measurements for this period. Add a reading from the server’s own dashboard.";
  return (
    <Card className="hw-metric">
      <div className="hw-card-heading">
        <h3>{title}</h3>
        {m.status && <HardwarePill status={m.status} stale={stale} />}
        {upsDevices?.some(({ component }) => component) && <div className="hw-ups-actions">
          {upsDevices.map(({ component }) => component && (
            <Button key={component.record.id} type="button" variant="ghost" size="icon" className="hw-ups-edit"
              aria-label={`Edit UPS component ${component.title}`} onClick={() => onEditUps?.(component.record.id)}>
              <Pencil aria-hidden="true" />
            </Button>
          ))}
        </div>}
      </div>
      {ups && (upsDevices?.length
        ? upsDevices.map(({ device, component, watts }) => <UpsReadings key={device.name} ups={ups} stale={stale} device={device}
            onSetup={onSetupUps}
            component={component} watts={watts}
            monthly={d.upsMonthlyPower?.find(summary => summary.deviceKey === device.key)}
            electricityRate={d.electricityRateUsdPerKwh} />)
        : <UpsReadings ups={ups} stale={stale} />)}
      {m.source === "none" ? !hasEstimate && (
        <>
          <strong className="hw-unmeasured-title">
            {type === "power"
              ? ups ? "Wattage not measured" : "Not measured"
              : d.readings.length
                ? "Not in your readings"
                : "No readings"}
          </strong>
          {!ups && <div className="hw-hatch" aria-hidden="true" />}
          {!integratedUps && <>
            <p>{reason}</p>
            <Button variant="link" onClick={onReading}>Add a reading</Button>
          </>}
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
                  className="hw-memory-bar hw-memory-free"
                  role="img"
                  aria-label={`Apps ${gb(value)}, ${cache} ${gb(m.cacheAverage)}, free ${gb(m.freeAverage)}`}
                >
                  <span
                    className="hw-memory-apps"
                    style={{ width: `${((value ?? 0) / d.totalBytes) * 100}%` }}
                  />
                  <span
                    className="hw-memory-cache"
                    style={{
                      width: `${(m.cacheAverage / d.totalBytes) * 100}%`,
                    }}
                  />
                </div>
                <p className="hw-memory-legend">
                  <span>
                    <i className="hw-memory-apps" aria-hidden="true" />
                    Apps {gb(value)}
                  </span>
                  <span>
                    <i className="hw-memory-cache" aria-hidden="true" />
                    {cache} {gb(m.cacheAverage)}
                  </span>
                  <span>
                    <i className="hw-memory-free" aria-hidden="true" />
                    Free {gb(m.freeAverage)}
                  </span>
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
      <Link to="/settings/review-levels">Change in Settings</Link>
    </p>
  );
}
