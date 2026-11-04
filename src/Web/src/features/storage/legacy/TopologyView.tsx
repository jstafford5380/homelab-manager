import {capabilityLabel} from "../../../lib/capabilityLabels";
import {DatasetUsageView} from "./DatasetUsageView";
import {PoolCapacityView} from "./PoolCapacityView";
import {CachedTemperaturesView} from "./CachedTemperaturesView";
import {CollectionHistoryView} from "../../activity/legacy/CollectionHistoryView";
import {PoolHistoryView} from "./PoolHistoryView";
import {lazy,Suspense,useCallback,useEffect,useRef,useState} from "react";
import {Button} from "@/components/ui/button";
import {Card} from "@/components/ui/card";
import {Badge} from "@/components/ui/badge";
import {Database,AlertTriangle} from "lucide-react";

const DiscoveryReviewView = lazy(() => import("./DiscoveryReviewView"));

type Node = {
  type?: string;
  name?: string;
  path?: string;
  guid?: string;
  status?: string;
  children?: Node[];
  stats?: Record<string, string | number | null>;
};
type Pool = {
  name: string;
  guid: string;
  status: string;
  topology: Record<string, Node[]> | null;
};
type Redundancy = {
  poolGuid: string; status: string; explanation: string;
  groups: { role: string; guid: string; layout: string; members: number; configuredTolerance: number | null; state: string }[];
};
type State = {
  systems: {
    id: string;
    name: string;
    status: string;
    lastSuccessUtc: string | null;
    topologyJson: string | null;
    redundancy?: Redundancy[];
  }[];
  capabilities: {
    connectionId: string;
    name: string;
    status: string;
    detail: string;
    lastSuccessUtc: string | null;
  }[];
  nativeAlerts: {
    id: number;
    connectionId: string;
    level: string;
    class: string;
    message: string;
    dismissedOnNas: boolean;
    absentSinceUtc: string | null;
  }[];
};
function validStorage(value: unknown): value is State {
  const object = (v: unknown): v is Record<string, unknown> =>
    v !== null && typeof v === "object" && !Array.isArray(v);
  const text = (v: unknown) => typeof v === "string";
  const optionalText = (v: unknown) => v == null || text(v);
  const time = (v: unknown) =>
    v === null || (typeof v === "string" && Number.isFinite(Date.parse(v)));
  if (
    !object(value) ||
    !Array.isArray(value.systems) ||
    !Array.isArray(value.capabilities) ||
    !Array.isArray(value.nativeAlerts)
  )
    return false;
  let nodeCount = 0;
  function node(v: unknown, depth = 0): boolean {
    if (!object(v) || depth > 20 || ++nodeCount > 10000) return false;
    if (
      !["type", "name", "path", "guid", "status"].every((key) =>
        optionalText(v[key]),
      )
    )
      return false;
    if (
      v.stats != null &&
      (!object(v.stats) ||
        !Object.values(v.stats).every(
          (x) =>
            x === null ||
            typeof x === "string" ||
            (typeof x === "number" && Number.isFinite(x)),
        ))
    )
      return false;
    return (
      v.children == null ||
      (Array.isArray(v.children) &&
        v.children.every((child) => node(child, depth + 1)))
    );
  }
  const systems = value.systems.every((v) => {
    if (
      !object(v) ||
      ![v.id, v.name, v.status].every(text) ||
      !time(v.lastSuccessUtc) ||
      !optionalText(v.topologyJson)
    )
      return false;
    if (v.redundancy !== undefined && (!Array.isArray(v.redundancy) || !v.redundancy.every(r =>
      object(r) && [r.poolGuid, r.status, r.explanation].every(text) && Array.isArray(r.groups) && r.groups.every(g =>
        object(g) && [g.role, g.guid, g.layout, g.state].every(text) && Number.isInteger(g.members) &&
        (g.configuredTolerance === null || Number.isInteger(g.configuredTolerance)))))) return false;
    if (!v.topologyJson) return true;
    try {
      const pools: unknown = JSON.parse(v.topologyJson as string);
      return (
        Array.isArray(pools) &&
        pools.every(
          (pool) =>
            object(pool) &&
            [pool.name, pool.guid, pool.status].every(text) &&
            (pool.topology === null ||
              (object(pool.topology) &&
                Object.values(pool.topology).every(
                  (members) =>
                    Array.isArray(members) &&
                    members.every((member) => node(member)),
                ))),
        )
      );
    } catch {
      return false;
    }
  });
  return (
    systems &&
    value.capabilities.every(
      (v) =>
        object(v) &&
        [v.connectionId, v.name, v.status, v.detail].every(text) &&
        time(v.lastSuccessUtc),
    ) &&
    value.nativeAlerts.every(
      (v) =>
        object(v) &&
        typeof v.id === "number" &&
        [v.connectionId, v.level, v.class, v.message].every(text) &&
        typeof v.dismissedOnNas === "boolean" &&
        time(v.absentSinceUtc),
    )
  );
}

function TreeNode({ node, depth = 0 }: { node: Node; depth?: number }) {
  if (depth > 20) return <p>Topology depth exceeds display limit.</p>;
  return (
    <li>
      <div className="topology-node">
        <Database size={15} />
        <strong>
          {node.path || node.name || node.type || "Unidentified member"}
        </strong>
        <Badge variant="outline">{node.status || "Unknown state"}</Badge>
        <small>
          {node.type} · GUID {node.guid || "unavailable"}
        </small>
      </div>
      {node.stats && (
        <p className="muted">
          {["read_errors", "write_errors", "checksum_errors"]
            .map(
              (key) =>
                `${key.replace("_", " ")}: ${node.stats?.[key] ?? "unknown"}`,
            )
            .join(" · ")}
        </p>
      )}
      {Array.isArray(node.children) && node.children.length > 0 && (
        <ul>
          {node.children.map((child, index) => (
            <TreeNode
              key={child.guid || index}
              node={child}
              depth={depth + 1}
            />
          ))}
        </ul>
      )}
    </li>
  );
}
export function TopologyView({ timezone }: { timezone: string }) {
  const [state, setState] = useState<State | null>(null);
  const [error, setError] = useState("");
  const [loading, setLoading] = useState(true);
  const [loadedAt, setLoadedAt] = useState<Date | null>(null);
  const currentRequest = useRef<AbortController | null>(null);
  const mounted = useRef(false);
  const load = useCallback(async () => {
    currentRequest.current?.abort();
    const controller = new AbortController();
    currentRequest.current = controller;
    let timedOut = false;
    const timeout = setTimeout(() => {
      timedOut = true;
      controller.abort();
    }, 15000);
    setLoading(true);
    try {
      const response = await fetch("/api/v1/storage", {
        signal: controller.signal,
      });
      if (!response.ok)
        throw new Error("The storage view could not be refreshed.");
      const data: unknown = await response.json();
      if (!validStorage(data))
        throw new Error("The storage response could not be read safely.");
      if (mounted.current && currentRequest.current === controller) {
        setState(data);
        setError("");
        setLoadedAt(new Date());
      }
    } catch (e) {
      if (mounted.current && currentRequest.current === controller) {
        setError(
          timedOut
            ? "The storage refresh timed out after 15 seconds."
            : e instanceof Error && e.name !== "AbortError"
              ? e.message
              : "The storage view could not be refreshed.",
        );
      }
    } finally {
      clearTimeout(timeout);
      if (mounted.current && currentRequest.current === controller)
        setLoading(false);
    }
  }, []);
  useEffect(() => {
    mounted.current = true;
    void load();
    const timer = setInterval(() => void load(), 30000);
    return () => {
      mounted.current = false;
      currentRequest.current?.abort();
      clearInterval(timer);
    };
  }, [load]);
  return (
    <section
      style={{ marginBottom: 32 }}
      aria-label="Storage monitoring view"
      aria-busy={loading}
    >
      <div className="section-title storage-refresh-heading">
        <div>
          <h2>Storage topology & collection coverage</h2>
          <p className="muted">
            {loadedAt
              ? `View loaded ${loadedAt.toLocaleString(undefined, { timeZone: timezone })}. Source freshness is shown below.`
              : "Last-known evidence remains available offline."}
          </p>
        </div>
        <Button variant="outline" onClick={() => void load()}>
          Refresh storage view
        </Button>
      </div>
      {loading && (
        <p role="status" className="muted">
          Refreshing storage view…
        </p>
      )}
      {error && (
        <p className="error" role="alert">
          {error}{" "}
          {state
            ? "Showing the last successfully loaded storage evidence."
            : "No storage evidence has loaded. Retry when the control service is available."}
        </p>
      )}
      {state && (
        <>
          {state.systems.length === 0 && (
            <Card className="list-card">
              <p style={{ padding: 16 }} className="muted">
                No NAS enrolled yet. Topology will appear after a successful
                read.
              </p>
            </Card>
          )}
          {state.systems.map((system) => {
            let pools: Pool[] = [];
            try {
              pools = system.topologyJson
                ? JSON.parse(system.topologyJson)
                : [];
            } catch {
              /* malformed stored evidence remains unknown */
            }
            const capabilities = state.capabilities.filter(
              (c) => c.connectionId === system.id,
            );
            return (
              <Card
                className="list-card"
                key={system.id}
                style={{ marginBottom: 20 }}
              >
                <div className="row storage-system-summary">
                  <Database />
                  <div>
                    <h2>{system.name}</h2>
                    <small>
                      Last successful source read:{" "}
                      {system.lastSuccessUtc
                        ? new Date(system.lastSuccessUtc).toLocaleString(
                            undefined,
                            { timeZone: timezone },
                          )
                        : "Never"}
                    </small>
                  </div>
                  <Badge variant="outline">{system.status}</Badge>
                </div>
                <div className="coverage-grid">
                  {capabilities.length ? (
                    capabilities.map((c) => (
                      <div key={c.name}>
                        <strong>{capabilityLabel(c.name)}</strong>
                        <Badge variant="outline">{c.status}</Badge>
                        <p className="muted">{c.detail}</p>
                        <small>
                          Last successful check:{" "}
                          {c.lastSuccessUtc
                            ? new Date(c.lastSuccessUtc).toLocaleString(
                                undefined,
                                {
                                  timeZone: timezone,
                                },
                              )
                            : "Never"}
                        </small>
                      </div>
                    ))
                  ) : (
                    <p className="muted">Capabilities have not been checked.</p>
                  )}
                </div>
                <Suspense fallback={<p>Loading identity review…</p>}><DiscoveryReviewView key={system.id} connectionId={system.id} /></Suspense>
                <DatasetUsageView key={system.id} connectionId={system.id} />
                <CachedTemperaturesView
                  connectionId={system.id}
                  timezone={timezone}
                />
                <CollectionHistoryView
                  connectionId={system.id}
                  timezone={timezone}
                />
                {pools.map((pool) => (
                  <section className="pool" key={pool.guid || pool.name}>
                    <div className="section-title">
                      <h3>{pool.name}</h3>
                      <Badge variant="outline">
                        {pool.status || "Unknown"}
                      </Badge>
                    </div>
                    <p className="muted">
                      Pool GUID: {pool.guid || "Unavailable"}
                    </p>
                    <details className="redundancy-context">
                      <summary>Redundancy context: {system.redundancy?.find(r => r.poolGuid === pool.guid)?.status ?? "unavailable"}</summary>
                      <p className="muted">{system.redundancy?.find(r => r.poolGuid === pool.guid)?.explanation ?? "No supported redundancy assessment is available for this evidence."}</p>
                      {system.redundancy?.find(r => r.poolGuid === pool.guid)?.groups.map(group => (
                        <div key={group.guid} className="redundancy-group">
                          <strong>{group.role} · {group.layout}</strong>
                          <p>{group.members} reported members · {group.state}</p>
                          <p>{group.configuredTolerance === null ? "Cache and spare roles do not add data redundancy." : `Configured group tolerance: ${group.configuredTolerance} disk failure(s). This is not a remaining-safe-removals count.`}</p>
                          <small>Vdev GUID: {group.guid}</small>
                        </div>
                      ))}
                    </details>
                    {pool.guid && (
                      <>
                        <PoolCapacityView
                          connectionId={system.id}
                          guid={pool.guid}
                          timezone={timezone}
                        />
                        <PoolHistoryView
                          connectionId={system.id}
                          guid={pool.guid}
                          timezone={timezone}
                        />
                      </>
                    )}
                    {pool.topology ? (
                      Object.entries(pool.topology).map(([role, members]) => (
                        <div key={role}>
                          <h4 className="topology-role">{role}</h4>
                          <ul className="topology-tree">
                            {Array.isArray(members) ? (
                              members.map((node, index) => (
                                <TreeNode
                                  key={node.guid || index}
                                  node={node}
                                />
                              ))
                            ) : (
                              <li>Unsupported topology representation</li>
                            )}
                          </ul>
                        </div>
                      ))
                    ) : (
                      <p className="muted">Topology unavailable.</p>
                    )}
                  </section>
                ))}
                <div className="section-title" style={{ marginTop: 20 }}>
                  <h3>Native TrueNAS alerts</h3>
                  <small>
                    Local acknowledgments never dismiss these alerts.
                  </small>
                </div>
                {state.nativeAlerts
                  .filter((a) => a.connectionId === system.id)
                  .map((a) => (
                    <div className="row" key={a.id}>
                      <AlertTriangle size={16} />
                      <div>
                        <h3>
                          {a.class} · {a.level}
                        </h3>
                        <p>{a.message}</p>
                        <small>
                          {a.absentSinceUtc
                            ? "Absent from latest successful NAS alert snapshot"
                            : a.dismissedOnNas
                              ? "Dismissed on NAS"
                              : "Present on NAS"}
                        </small>
                      </div>
                    </div>
                  ))}
                {!state.nativeAlerts.some(
                  (a) => a.connectionId === system.id,
                ) && (
                  <p className="muted" style={{ paddingBottom: 20 }}>
                    No native alerts recorded. Check alert collection coverage
                    above.
                  </p>
                )}
              </Card>
            );
          })}
        </>
      )}
    </section>
  );
}
