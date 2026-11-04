import {useClock} from "../../lib/useClock";
import {useEffect,useState} from "react";
import {relative,storageRequest,type Drive,type Server} from "./storageModel";
function powerOnDuration(hours: number): string {
  const totalMonths = Math.floor(hours / (24 * 365.25 / 12));
  if (totalMonths < 1) return "Less than 1 month";
  const years = Math.floor(totalMonths / 12), months = totalMonths % 12;
  const parts = [
    years ? `${years} ${years === 1 ? "year" : "years"}` : "",
    months ? `${months} ${months === 1 ? "month" : "months"}` : "",
  ].filter(Boolean);
  return "About " + parts.join(", ");
}

type Trend = {
  metric: string;
  segments: { atUtc: string; value: number }[][];
  explanation: string;
};
export function StorageHealth({
  drive: d,
  server,
  smartEnabled,
}: {
  drive: Drive;
  server: Server;
  smartEnabled: boolean;
}) {
  const now=useClock();
  const [trends, setTrends] = useState<Trend[]>([]),
    [error, setError] = useState("");
  useEffect(() => {
    if (!smartEnabled) return;
    const controller = new AbortController();
    void storageRequest(
      "/storage/drives/" + d.disk.id + "/trends",
      undefined,
      "GET",
      controller.signal,
    )
      .then((r) => {
        if (!controller.signal.aborted) setTrends(r.trends);
      })
      .catch(() => {
        if (!controller.signal.aborted)
          setError("Error-counter history is unavailable.");
      });
    return () => controller.abort();
  }, [d.disk.id, smartEnabled]);
  const s = d.smart,
    stale = server.freshness !== "Reporting";
  const line = !smartEnabled
    ? "From ZFS and TrueNAS. No SMART details yet."
    : !s?.readUtc
      ? "No SMART reading is available yet."
      : stale
        ? "Last SMART reading " +
          relative(s.readUtc) +
          ", before this server stopped reporting."
        : s.skippedAsleep
          ? "Asleep, so it was skipped to avoid waking it. Last SMART reading " +
            relative(s.readUtc) +
            "."
          : "SMART read " +
            relative(s.readUtc) +
            " · last self-test " +
            (s.lastSelfTest?.entries[0]?.result || "unknown") +
            " · self-test time unknown";
  const tiles: { label: string; value: string; equivalent?: string; detail?: string }[] = [];
  if (smartEnabled)
    tiles.push(
      {
        label: "SMART",
        value:
          s?.passed == null
            ? "Unknown"
            : (s.passed ? "Passed" : "Failed") +
              (stale ||
              s.skippedAsleep ||
              s.error ||
              (s.readUtc && now - Date.parse(s.readUtc) > 36 * 3600000)
                ? " (last known)"
                : ""),
      },
    );
  else
    tiles.push({
      label: "Pool member",
      value: d.memberState || (d.poolGuid ? "Unknown" : "Not in a pool"),
      detail: stale ? "Last known" : "Reported by ZFS",
    });
  const apiHours = d.apiPowerOnHours;
  if (smartEnabled || apiHours) {
    const sshHours = smartEnabled ? s?.powerOnHours : null;
    tiles.push({
      label: "Powered on",
      equivalent: (sshHours ?? apiHours?.hours) != null ? powerOnDuration((sshHours ?? apiHours?.hours)!) : undefined,
      value: sshHours != null ? sshHours.toLocaleString() + " hours" :
        apiHours?.hours != null ? apiHours.hours.toLocaleString() + " hours" + (apiHours.status !== "supported" || stale ? " (last known)" : "") :
        apiHours?.status === "unsupported" ? "Not supported via API" :
        apiHours?.status === "disabled" ? "Collection disabled" :
        apiHours?.status === "denied" ? "Permission denied" : "Unknown",
      detail: (sshHours ?? apiHours?.hours) != null ? undefined :
        (apiHours?.detail ?? "No power-on hours reading"),
    });
  }
  tiles.push(
    {
      label: "ZFS errors",
      value:
        (d.readErrors ?? "Unknown") +
        " · " +
        (d.writeErrors ?? "Unknown") +
        " · " +
        (d.checksumErrors ?? "Unknown"),
      detail: "Read · write · checksum",
    },
    {
      label: "Temperature",
      value:
        (smartEnabled ? (s?.temperatureC ?? d.temperatureC) : d.temperatureC) ==
        null
          ? d.temperatureNote === "Collection disabled in Settings."
            ? "Collection disabled"
            : d.temperatureNote ===
                "TrueNAS returned an empty temperature cache."
              ? "No cached reading"
              : "Unknown"
          : (smartEnabled
              ? (s?.temperatureC ?? d.temperatureC)
              : d.temperatureC) + " °C",
      detail:
        smartEnabled && s?.temperatureC != null
          ? "From SMART · " + relative(s.readUtc)
          : d.temperatureC == null
            ? (d.temperatureNote ?? "No recent reading")
            : "NAS cache, fetched " + relative(d.temperatureFetchedUtc),
    },
  );
  if (smartEnabled) {
    tiles.push(
      s?.nvmeWearPercent != null
        ? {
            label: "SSD wear",
            value: s.nvmeWearPercent + "%",
            detail: "Manufacturer rating",
          }
        : {
            label: "Pending sectors",
            value: s?.pendingSectors?.toString() ?? "Unknown",
          },
    );
    tiles.push({
      label: "Reallocated sectors",
      value: s?.reallocatedSectors?.toString() ?? "Unknown",
    });
  }
  if (smartEnabled) {
    const errors = tiles.splice(
      tiles.findIndex((t) => t.label === "ZFS errors"),
      1,
    )[0];
    tiles.push(errors);
  }
  return (
    <section aria-labelledby="storage-health">
      <h3 id="storage-health">Health</h3>
      <small>{line}</small>
      {smartEnabled && s?.error && (
        <p className="storage-secondary">{s.error}</p>
      )}
      <dl className="storage-tiles">
        {tiles.map((t) => (
          <div key={t.label}>
            <dt>{t.label}</dt>
            <dd>{t.value}</dd>
            {t.equivalent && <dd className="storage-tile-detail">{t.equivalent}</dd>}
            {t.detail && <dd className="storage-tile-detail">{t.detail}</dd>}
          </div>
        ))}
      </dl>
      {smartEnabled && error && <p>{error}</p>}
      {smartEnabled &&
        trends.map((t) => {
          const pts = t.segments.flat(),
            min = Math.min(...pts.map((p) => Date.parse(p.atUtc))),
            span = Math.max(
              1,
              Math.max(...pts.map((p) => Date.parse(p.atUtc))) - min,
            ),
            max = Math.max(1, ...pts.map((p) => p.value));
          const x = (p: { atUtc: string }) =>
              5 + ((Date.parse(p.atUtc) - min) / span) * 290,
            y = (p: { value: number }) => 55 - (p.value / max) * 45;
          return (
            <figure className="storage-box" key={t.metric}>
              <figcaption>
                {t.metric.replaceAll("_", " ")} · last 8 weeks
              </figcaption>
              <svg
                viewBox="0 0 300 65"
                role="img"
                aria-label={
                  t.metric +
                  ": " +
                  pts.map((p) => p.atUtc + " " + p.value).join(", ")
                }
              >
                {t.segments.map((segment, i) => (
                  <g key={i}>
                    <polyline
                      points={segment.map((p) => x(p) + "," + y(p)).join(" ")}
                      fill="none"
                      stroke="var(--tone-warn)"
                      strokeWidth="2"
                    />
                    {segment.map((p, j) => (
                      <circle
                        key={j}
                        cx={x(p)}
                        cy={y(p)}
                        r="2.5"
                        fill="var(--tone-warn)"
                      />
                    ))}
                  </g>
                ))}
              </svg>
              <small>{t.explanation}</small>
            </figure>
          );
        })}
    </section>
  );
}
