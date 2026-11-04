import { Link } from "react-router";
import { ChevronRight } from "lucide-react";
import { Card } from "@/components/ui/card";
import { useDisplayFormat } from "../../lib/displayPreferences";
import { type Drive, type Pool } from "./storageModel";
import { StoragePill, StorageBar } from "./StorageIndicators";
import { DriveTable } from "./DriveTable";
import {
  scanLine,
  forecastLine,
  resilverEstimate,
} from "./storagePresentation";

export function PoolCard({
  pool,
  stale,
  timezone,
  open,
}: {
  pool: Pool;
  stale: boolean;
  timezone: string;
  open: (d: Drive, edit?: boolean) => void;
}) {
  const { bytes } = useDisplayFormat();
  const percent =
    pool.usedBytes != null && pool.sizeBytes != null && pool.sizeBytes > 0
      ? (pool.usedBytes / pool.sizeBytes) * 100
      : null;
  const datasets = pool.datasets.slice(0, 6),
    other = pool.datasets.slice(6);
  if (other.length)
    datasets.push({
      name: "Other",
      usedBytes: other.every((x) => x.usedBytes != null)
        ? other.reduce((a, x) => a + x.usedBytes!, 0)
        : null,
    });
  const max = Math.max(1, ...datasets.map((d) => d.usedBytes ?? 0));
  return (
    <Card className="storage-pool">
      <div className="storage-pool-head">
        <div className="storage-pool-meta">
          <div className="storage-inline">
            <h3>{pool.name}</h3>
            <StoragePill status={pool.status} />
          </div>
          <p className="storage-secondary">
            {pool.layout} · {pool.memberCount} drives
            {pool.toleratedFailures != null
              ? " · survives " +
                pool.toleratedFailures +
                " drive loss" +
                (pool.toleratedFailures === 1 ? "" : "es")
              : ""}
          </p>
          <p
            className={
              "storage-scan" +
              (!stale &&
              pool.scan?.scanFunction === "RESILVER" &&
              pool.scan.scanState === "SCANNING"
                ? " storage-scan-active"
                : "")
            }
          >
            <span
              className={
                "storage-dot storage-tone-" +
                (stale ? "neutral" : pool.scan?.scanErrors ? "warn" : "ok")
              }
              aria-hidden="true"
            />
            {scanLine(pool, stale)}
          </p>
        </div>
        <div className="storage-usage">
          <div className="storage-between">
            <span>
              <strong>{bytes(pool.usedBytes)}</strong> used of{" "}
              {bytes(pool.sizeBytes)}
            </span>
            <span className="storage-secondary">
              {percent == null
                ? "Usage unknown"
                : Math.round(percent) + "% full"}
            </span>
          </div>
          <StorageBar
            value={percent}
            tone={
              percent == null
                ? undefined
                : percent >= 95
                  ? "bad"
                  : percent >= 85
                    ? "warn"
                    : "ok"
            }
            label={
              percent == null
                ? "Pool usage unknown"
                : Math.round(percent) + " percent used"
            }
          />
          <small>{forecastLine(pool, stale, timezone)}</small>
        </div>
      </div>
      {pool.unmappedMembers > 0 && (
        <p className="storage-pool-note">
          {pool.unmappedMembers} pool member
          {pool.unmappedMembers === 1 ? "" : "s"} could not be matched to a
          physical drive. Membership remains unknown.
        </p>
      )}
      <details className="storage-datasets">
        <summary>
          <ChevronRight size={15} />
          What's using space
        </summary>
        {pool.datasetStatus === "disabled" ? (
          <p>
            Dataset usage is turned off. Turn it on in{" "}
            <Link to="/settings">Settings</Link>.
          </p>
        ) : (
          <>
            {pool.datasetStatus !== "supported" && (
              <p>
                {datasets.length
                  ? "Last known dataset usage. Current usage is unavailable."
                  : "Dataset usage is unavailable."}
              </p>
            )}
            {pool.datasetStatus === "supported" && datasets.length === 0 && (
              <p>TrueNAS returned no dataset usage.</p>
            )}
            {datasets.length === 1 && datasets[0].name === pool.name && (
              <p>
                Only the root dataset was returned. A folder-by-folder breakdown
                isn't available from this read.
              </p>
            )}
            <ul>
              {datasets.map((d) => (
                <li key={d.name}>
                  <div className="storage-between">
                    <span className="storage-mono">{d.name}</span>
                    <span>{bytes(d.usedBytes)}</span>
                  </div>
                  <StorageBar
                    value={
                      d.usedBytes == null ? null : (d.usedBytes / max) * 100
                    }
                    label={d.name + " uses " + bytes(d.usedBytes)}
                  />
                </li>
              ))}
            </ul>
          </>
        )}
      </details>
      <DriveTable
        drives={pool.drives}
        open={open}
        estimate={resilverEstimate(pool, stale)}
      />
    </Card>
  );
}
