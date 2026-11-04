import { HardwareMetricCard, HardwareReviewLine } from "./HardwareCharts";
import { type HwDetail } from "./hardwareModel";
import { useDisplayFormat } from "../../lib/displayPreferences";

export function HardwareMetrics({
  d,
  range,
  stale,
  setRange,
  onReading,
  onSetupUps,
  onEdit,
}: {
  d: HwDetail;
  range: string;
  stale: boolean;
  setRange: (range: string) => void;
  onReading: () => void;
  onSetupUps: (key: string) => void;
  onEdit: (id: string) => void;
}) {
  const { time } = useDisplayFormat();
  return (
    <section aria-labelledby="hw-running">
      <div className="hw-section-heading">
        <div>
          <h2 id="hw-running">How it’s running</h2>
          <p className="hw-coverage">
            {d.lastCollectionUtc
              ? `Last collection run · ${time(d.lastCollectionUtc)}`
              : "No collection run recorded"}
          </p>
        </div>
        <div className="hw-ranges" role="group" aria-label="Time range">
          {[
            ["24h", "24 h"],
            ["7d", "7 days"],
            ["30d", "30 days"],
            ["90d", "90 days"],
          ].map(([key, label]) => (
            <button
              key={key}
              aria-pressed={range === key}
              onClick={() => setRange(key)}
            >
              {label}
            </button>
          ))}
        </div>
      </div>
      <div className="hw-metrics">
        {(["cpu", "memory", "temperature", "power"] as const).map((type) => (
          <HardwareMetricCard
            key={type}
            title={
              type === "cpu"
                ? "CPU use"
                : type === "memory"
                  ? "Memory"
                  : type === "power"
                    ? "Power draw"
                    : d.server.platform === "synology"
                      ? "System temperature"
                      : "CPU temperature"
            }
            metric={d[type]}
            detail={d}
            type={type}
            stale={stale}
            onReading={() => onReading()}
            onSetupUps={(key) => {
              onSetupUps(key);
            }}
            onEditUps={onEdit}
          />
        ))}
      </div>
      <HardwareReviewLine detail={d} />
    </section>
  );
}
