import { formatInvestment } from "./investment";
import type { ServerRefreshTarget } from "./RefreshServerButton";
import { useCallback, useEffect, useRef, useState } from "react";
import { Link, useSearchParams } from "react-router";
import {
  HardDrive,
  Check,
  ChevronRight,
  AlertTriangle,
  CircleCheck,
  TriangleAlert,
  CircleAlert,
  CircleHelp,
  type LucideIcon,
} from "lucide-react";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Card } from "@/components/ui/card";
import { useDisplayFormat } from "./displayPreferences";
import { StorageDrivePanel } from "./StorageDrivePanel";
import {
  age,
  allDrives,
  driveKind,
  relative,
  storageRequest,
  validStorageData,
  type Drive,
  type Pool,
  type Status,
  type StorageData,
} from "./storageModel";
import "./Storage.css";

const pillIcon: Record<string, LucideIcon> = {
  ok: CircleCheck,
  warn: TriangleAlert,
  bad: CircleAlert,
  neutral: CircleHelp,
};
export function StoragePill({ status }: { status: Status }) {
  const Icon = pillIcon[status.tone] ?? CircleHelp;
  return (
    <Badge
      variant="outline"
      className={"storage-pill storage-tone-" + status.tone}
    >
      <Icon aria-hidden="true" />
      {status.label}
    </Badge>
  );
}
export function StorageBar({
  value,
  label,
  tone,
}: {
  value: number | null;
  label: string;
  tone?: "ok" | "warn" | "bad";
}) {
  return (
    <div className="storage-bar" role="img" aria-label={label}>
      <span
        style={{
          width: Math.max(0, Math.min(100, value ?? 0)) + "%",
          backgroundColor: tone ? `var(--tone-${tone})` : undefined,
        }}
      />
    </div>
  );
}
function scanLine(pool: Pool, stale: boolean) {
  const s = pool.scan;
  const progress =
    s?.scanPercentage == null
      ? "progress unknown"
      : Math.round(s.scanPercentage) + "%";
  const line = !s?.scanFunction
    ? "No scrub recorded"
    : s.scanState === "SCANNING" || s.scanState === "PAUSED"
      ? (s.scanFunction === "RESILVER"
          ? "Resilvering · "
          : "Scrub in progress · ") +
        progress +
        (s.scanState === "PAUSED" ? " · paused" : "") +
        (s.scanStartedUtc ? " · started " + relative(s.scanStartedUtc) : "")
      : s.scanEndedUtc
        ? (s.scanFunction === "SCRUB" ? "Last scrub " : "Last resilver ") +
          relative(s.scanEndedUtc) +
          " · " +
          (s.scanErrors ?? "Unknown") +
          " errors"
        : "No scrub recorded";
  return line + (stale ? " (last known)" : "");
}
function forecastLine(pool: Pool, stale: boolean, timezone: string) {
  const o = pool.capacityOutlook,
    threshold = Math.round((o?.thresholdFraction ?? 0.8) * 100);
  if (stale || o?.status === "stale" || o?.status === "paused")
    return "Forecast paused until this server reports again";
  switch (o?.status) {
    case "estimated":
      return o.projectedDateAtThreshold
        ? "At the current rate, " +
            threshold +
            "% full around " +
            new Date(o.projectedDateAtThreshold).toLocaleDateString("en-US", {
              month: "short",
              year: "numeric",
              timeZone: timezone,
            })
        : "No forecast available";
    case "threshold-reached":
      return "Already past " + threshold + "% full";
    case "not-growing":
      return "Not growing, so no fill date to show";
    case "insufficient":
      return "Not enough history yet for a forecast";
    default:
      return "No forecast available";
  }
}
function DriveTable({
  drives,
  open,
}: {
  drives: Drive[];
  open: (d: Drive, edit?: boolean) => void;
}) {
  const { bytes } = useDisplayFormat();
  const grouped = drives.some((d) => d.group != null);
  const buckets = new Map<
    string,
    { id: string; name: string | null; role: string; drives: Drive[] }
  >();
  for (const d of drives) {
    const id = d.group ? "known:" + d.group.id : "unknown";
    if (!buckets.has(id))
      buckets.set(id, {
        id,
        name: d.group?.name ?? (grouped ? "Group unknown" : null),
        role: d.group?.role ?? "unknown",
        drives: [],
      });
    buckets.get(id)!.drives.push(d);
  }
  const roles = [
    "data",
    "special",
    "dedup",
    "log",
    "cache",
    "spare",
    "unknown",
  ];
  const groups = Array.from(buckets.values()).sort((a, b) => {
    const rank = (role: string) =>
      roles.includes(role) ? roles.indexOf(role) : roles.length;
    return (
      rank(a.role) - rank(b.role) ||
      (a.name ?? "").localeCompare(b.name ?? "", undefined, { numeric: true })
    );
  });
  const groupHeading = (group: (typeof groups)[number]) => (
    <div className="storage-group-label">
      <strong>{group.name}</strong>
      <span>
        {group.role !== "unknown" && group.role + " · "}
        {group.drives.length} {group.drives.length === 1 ? "drive" : "drives"}
      </span>
    </div>
  );
  return (
    <>
      <div className="storage-table">
        <table>
          <thead>
            <tr>
              {[
                "Status",
                "Drive",
                "Capacity",
                "Serial",
                "Location",
                "In service",
              ].map((h) => (
                <th scope="col" key={h}>
                  {h}
                </th>
              ))}
            </tr>
          </thead>
          {groups.map((group, index) => (
            <tbody
              key={group.id}
              className={
                grouped
                  ? "storage-drive-group storage-group-" + (index % 2)
                  : undefined
              }
            >
              {group.name && (
                <tr className="storage-group-heading">
                  <th scope="rowgroup" colSpan={6}>
                    {groupHeading(group)}
                  </th>
                </tr>
              )}
              {group.drives.map((d) => (
                <tr key={d.disk.id}>
                  <td>
                    <StoragePill status={d.status} />
                  </td>
                  <td>
                    <button
                      className="storage-drive-link"
                      data-drive-id={d.disk.id}
                      onClick={() => open(d)}
                    >
                      {d.title}
                    </button>
                    <small>{driveKind(d)}</small>
                  </td>
                  <td>{bytes(d.disk.sizeBytes)}</td>
                  <td className="storage-mono">{d.disk.serial || "Unknown"}</td>
                  <td>
                    <strong>{d.disk.bay || "Not recorded"}</strong>
                    <small
                      className={
                        d.bayConfirmed
                          ? "storage-confirmed"
                          : "storage-unconfirmed"
                      }
                    >
                      {d.bayConfirmed && <Check size={12} />}{" "}
                      {d.bayConfirmed ? "Confirmed" : "Not confirmed"}
                    </small>
                    {d.disk.enclosure != null && d.disk.slot != null && (
                      <small>
                        Enclosure {d.disk.enclosure} · Slot {d.disk.slot}
                      </small>
                    )}
                  </td>
                  <td>
                    {age(d.acquisition?.installedUtc) || (
                      <button
                        className="storage-link"
                        onClick={() => open(d, true)}
                      >
                        Add install date
                      </button>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          ))}
        </table>
      </div>
      <div className="storage-drive-cards">
        {groups.map((group, index) => (
          <div
            key={group.id}
            className={
              grouped
                ? "storage-drive-group storage-group-" + (index % 2)
                : undefined
            }
          >
            {group.name && (
              <div className="storage-group-heading">{groupHeading(group)}</div>
            )}
            {group.drives.map((d) => (
              <button
                key={d.disk.id}
                className="storage-drive-card"
                data-drive-id={d.disk.id}
                onClick={() => open(d)}
              >
                <strong>{d.title}</strong>
                <StoragePill status={d.status} />
                <span>
                  {d.disk.bay || "Location unknown"} · {bytes(d.disk.sizeBytes)}{" "}
                  · {d.disk.serial || "Serial unknown"}
                </span>
                <span>
                  {d.bayConfirmed ? "Confirmed" : "Not confirmed"} ·{" "}
                  {age(d.acquisition?.installedUtc) || "Add install date"}
                </span>
              </button>
            ))}
          </div>
        ))}
      </div>
    </>
  );
}
function PoolCard({
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
          <p className="storage-scan">
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
            tone={percent == null ? undefined : percent >= 95 ? "bad" : percent >= 85 ? "warn" : "ok"}
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
      <DriveTable drives={pool.drives} open={open} />
    </Card>
  );
}
export function StorageView({
  timezone,
  refresh,
  refreshRevision,
  onRefreshTargetChange,
}: {
  disks: unknown[];
  timezone: string;
  refresh: () => Promise<void>;
  refreshRevision: number | null;
  onRefreshTargetChange: (target: ServerRefreshTarget | null) => void;
}) {
  const [data, setData] = useState<StorageData | null>(null),
    [error, setError] = useState(""),
    [loaded, setLoaded] = useState<string | null>(null);
  const [params, setParams] = useSearchParams(),
    [editing, setEditing] = useState(false);
  const [mobile, setMobile] = useState(
    () => window.matchMedia("(max-width: 700px)").matches,
  );
  const openedDisk = useRef<string | null>(null);
  const opener = useRef<HTMLElement | null>(null),
    request = useRef<AbortController | null>(null),
    mounted = useRef(false);
  const { bytes } = useDisplayFormat();
  const load = useCallback(async () => {
    request.current?.abort();
    const controller = new AbortController();
    request.current = controller;
    const timeout = setTimeout(() => controller.abort(), 15000);
    try {
      const next = await storageRequest(
        "/storage/overview",
        undefined,
        "GET",
        controller.signal,
      );
      if (!validStorageData(next))
        throw new Error("The storage response could not be read safely.");
      if (mounted.current && request.current === controller) {
        setData(next);
        setLoaded(new Date().toISOString());
        setError("");
      }
    } catch (e) {
      if (mounted.current && request.current === controller)
        setError(
          controller.signal.aborted
            ? "The request timed out."
            : e instanceof Error
              ? e.message
              : String(e),
        );
    } finally {
      clearTimeout(timeout);
    }
  }, []);
  useEffect(() => {
    mounted.current = true;
    void load();
    const timer = setInterval(() => void load(), 30000);
    return () => {
      mounted.current = false;
      clearInterval(timer);
      request.current?.abort();
    };
  }, [load]);
  const lastRevision = useRef(refreshRevision);
  useEffect(() => {
    if (lastRevision.current !== refreshRevision) {
      lastRevision.current = refreshRevision;
      void load();
    }
  }, [refreshRevision, load]);
  useEffect(() => {
    const media = window.matchMedia("(max-width: 700px)");
    const change = () => setMobile(media.matches);
    media.addEventListener("change", change);
    return () => media.removeEventListener("change", change);
  }, []);
  const diskId = params.get("disk");
  const diskServer = data?.servers.find((s) =>
    allDrives(s).some((d) => d.disk.id === diskId),
  );
  const server =
    diskServer ??
    data?.servers.find((s) => s.id === params.get("server")) ??
    data?.servers[0];
  const refreshServerId = server?.id;
  const refreshFreshness = server?.freshness;
  useEffect(() => {
    onRefreshTargetChange(refreshServerId && refreshFreshness !== "Paused" && refreshFreshness !== "Not connected"
      ? { serverId: refreshServerId, target: "storage" }
      : null);
    return () => onRefreshTargetChange(null);
  }, [onRefreshTargetChange, refreshServerId, refreshFreshness]);
  const drive =
    diskServer && allDrives(diskServer).find((d) => d.disk.id === diskId);
  const restoreFocus = () => {
    if (opener.current?.isConnected) opener.current.focus();
    else if (openedDisk.current)
      Array.from(document.querySelectorAll<HTMLElement>("[data-drive-id]"))
        .find(
          (el) =>
            el.dataset.driveId === openedDisk.current &&
            el.offsetParent !== null,
        )
        ?.focus();
  };
  const close = () => {
    setParams(server ? { server: server.id } : {});
    setEditing(false);
    requestAnimationFrame(restoreFocus);
  };
  const open = (d: Drive, edit = false) => {
    opener.current = document.activeElement as HTMLElement;
    openedDisk.current = d.disk.id;
    setEditing(edit);
    setParams({ server: d.disk.connectionId, disk: d.disk.id });
  };
  const saved = async () => {
    await load();
    await refresh();
  };
  return (
    <section className="storage-page" aria-label="Storage">
      {error && (
        <div role="status" className="storage-banner storage-tone-bad">
          <AlertTriangle size={20} />
          <div>
            <strong>Storage couldn't be loaded.</strong> {error}{" "}
            {loaded
              ? "Showing what was loaded " + relative(loaded) + "."
              : "Nothing has been loaded yet."}
          </div>
          <Button variant="outline" onClick={() => void load()}>
            Retry
          </Button>
        </div>
      )}
      {!data && !error && (
        <div
          role="region"
          aria-busy="true"
          aria-label="Loading storage"
          className="storage-stack"
        >
          <div className="storage-loading-tabs">
            <div className="storage-skeleton" />
            <div className="storage-skeleton" />
          </div>
          <div
            className="storage-skeleton"
            style={{ height: 28, width: "45%" }}
          />
          <div className="storage-facts storage-loading-facts">
            {[1, 2, 3, 4, 5].map((i) => (
              <div className="storage-skeleton" key={i} />
            ))}
          </div>
          <div className="storage-skeleton storage-skeleton-pool" />
        </div>
      )}
      {data && data.servers.length === 0 && (
        <Card className="storage-empty">
          <HardDrive size={32} />
          <h2>No servers yet</h2>
          <p>
            Connect a NAS or server in Settings and its pools and drives will
            appear here.
          </p>
          <Button asChild>
            <Link to="/settings#connections">Go to Settings</Link>
          </Button>
        </Card>
      )}
      {data && server && !(mobile && drive) && (
        <>
          <nav className="storage-tabs" aria-label="Servers">
            {data.servers.map((s) => (
              <button
                key={s.id}
                aria-pressed={s.id === server.id}
                onClick={() => setParams({ server: s.id })}
              >
                <span
                  aria-hidden="true"
                  className={
                    "storage-dot storage-tone-" +
                    (s.freshness !== "Reporting"
                      ? "neutral"
                      : s.attentionCount ||
                          s.pools.some((p) => p.status.tone === "bad")
                        ? "warn"
                        : "ok")
                  }
                />
                <span>
                  <strong>{s.name}</strong>
                  <small>
                    {s.freshness === "Out of date"
                      ? "Out of date · " + relative(s.lastSuccessUtc)
                      : s.freshness !== "Reporting"
                        ? s.freshness
                        : s.driveCount +
                          " drives · " +
                          (s.attentionCount
                            ? s.attentionCount + " to watch"
                            : s.pools.some((p) => p.status.label === "Degraded")
                              ? "pool needs a look"
                              : allDrives(s).some(
                                    (d) => d.status.label === "Unknown",
                                  )
                                ? "partly unknown"
                                : "all healthy")}
                  </small>
                </span>
              </button>
            ))}
          </nav>
          <section
            className="storage-stack"
            aria-labelledby="storage-server-title"
          >
            <div className="storage-server-head">
              <h2 id="storage-server-title">{server.name}</h2>
              <StoragePill
                status={{
                  label: server.freshness,
                  tone: server.freshness === "Reporting" ? "ok" : "neutral",
                  why: null,
                }}
              />
              <p className="storage-secondary">
                {server.host} · last collected {relative(server.lastSuccessUtc)}
              </p>
              {server.alerts.length > 0 && (
                <details className="storage-alerts">
                  <summary>
                    <StoragePill
                      status={{
                        label: server.alerts.length + " NAS alerts",
                        tone: "warn",
                        why: null,
                      }}
                    />
                  </summary>
                  <ul>
                    {server.alerts.map((a) => (
                      <li key={a.id}>
                        <strong>
                          {a.class} · {a.level}
                        </strong>
                        <p>{a.message}</p>
                        <small>
                          {relative(a.lastSeenUtc)} · reported by TrueNAS
                        </small>
                      </li>
                    ))}
                  </ul>
                </details>
              )}
            </div>
            {server.freshness === "Out of date" && (
              <div role="status" className="storage-banner storage-tone-warn">
                <AlertTriangle size={20} />
                <p>
                  <strong>Can't reach this server right now.</strong> Everything
                  below is from the last successful check,{" "}
                  {relative(server.lastSuccessUtc)}. Drive status may have
                  changed since.
                </p>
              </div>
            )}
            {server.freshness === "Paused" && (
              <p role="status" className="storage-banner storage-tone-neutral">
                Collection is paused. Everything below is last known.
              </p>
            )}
            <dl className="storage-facts">
              {[
                ["Pools", server.poolCount],
                ["Drives", server.driveCount],
                ["Raw capacity", bytes(server.rawCapacityBytes)],
                ["Needs a look", server.attentionCount ?? "Unknown"],
                ["Total Investment", formatInvestment(data.investment)],
              ].map(([label, value]) => (
                <div key={label}>
                  <dt>{label}</dt>
                  <dd>{value}</dd>
                </div>
              ))}
            </dl>
            {server.pools.map((p) => (
              <PoolCard
                key={server.id + p.guid}
                pool={p}
                stale={server.freshness !== "Reporting"}
                timezone={timezone}
                open={open}
              />
            ))}
            {server.looseDrives.length > 0 && (
              <Card className="storage-pool">
                <div className="storage-pool-head">
                  <div>
                    <h3>Not in a pool</h3>
                    <p className="storage-secondary">
                      Spares, boot drives and anything unassigned
                    </p>
                  </div>
                </div>
                <DriveTable drives={server.looseDrives} open={open} />
              </Card>
            )}
            {!server.pools.length && !server.looseDrives.length && (
              <Card className="storage-empty">
                <HardDrive />
                <h3>No pools or drives collected yet</h3>
                <p>
                  This server's storage will appear after a successful
                  collection.
                </p>
              </Card>
            )}
          </section>
        </>
      )}
      {data && server && drive && (
        <StorageDrivePanel
          key={drive.disk.id + ":" + mobile}
          drive={drive}
          server={server}
          data={data}
          mobile={mobile}
          editingInitially={editing}
          close={close}
          restoreFocus={restoreFocus}
          saved={saved}
        />
      )}
      {data && diskId && !drive && (
        <p role="status">
          This drive could not be found in this installation.{" "}
          <button className="storage-link" onClick={close}>
            Back to storage
          </button>
        </p>
      )}
    </section>
  );
}
