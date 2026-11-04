import { formatInvestment } from "../../lib/investment";
import type { ServerRefreshTarget } from "../../components/RefreshServerButton";
import { useEffect, useRef, useState } from "react";
import { Link, useSearchParams } from "react-router";
import { HardDrive, AlertTriangle } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Card } from "@/components/ui/card";
import { useDisplayFormat } from "../../lib/displayPreferences";
import { StorageDrivePanel } from "./StorageDrivePanel";
import { allDrives, relative, type Drive } from "./storageModel";
import "./Storage.css";
import { StoragePill } from "./StorageIndicators";
import { DriveTable } from "./DriveTable";
import { PoolCard } from "./PoolCard";
import { useStorageInventory } from "./useStorageInventory";

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
  const { data, error, loaded, load } = useStorageInventory(refreshRevision);
  const [params, setParams] = useSearchParams(),
    [editing, setEditing] = useState(false);
  const [mobile, setMobile] = useState(
    () => window.matchMedia("(max-width: 700px)").matches,
  );
  const openedDisk = useRef<string | null>(null);
  const opener = useRef<HTMLElement | null>(null);
  const { bytes } = useDisplayFormat();
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
    onRefreshTargetChange(
      refreshServerId &&
        refreshFreshness !== "Paused" &&
        refreshFreshness !== "Not connected"
        ? { serverId: refreshServerId, target: "storage" }
        : null,
    );
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
