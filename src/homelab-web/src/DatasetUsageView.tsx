import { DatasetHistoryView } from "./DatasetHistoryView";
import { useEffect, useId, useState } from "react";
import { Link } from "react-router";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Card } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { useDisplayFormat } from "./displayPreferences";

type Usage = { name: string; pool: string; type: "FILESYSTEM" | "VOLUME";
  usedBytes: number | null; availableBytes: number | null; snapshotUsedBytes: number | null;
  quotaBytes: number | null; refQuotaBytes: number | null };
type State = { enabled: boolean; status: string; validUntilUtc: string | null;
  snapshot: { fetchedUtc: string; sourceSchema: string; items: Usage[] } | null };
function valid(value: unknown): value is State {
  const object = (v: unknown): v is Record<string, unknown> => v !== null && typeof v === "object" && !Array.isArray(v);
  const date = (v: unknown) => typeof v === "string" && Number.isFinite(Date.parse(v));
  if (!object(value) || typeof value.enabled !== "boolean" || typeof value.status !== "string" ||
    !(value.validUntilUtc === null || date(value.validUntilUtc))) return false;
  if (value.snapshot === null) return true;
  const snapshot = value.snapshot;
  if (!object(snapshot) || !date(snapshot.fetchedUtc) || typeof snapshot.sourceSchema !== "string" ||
    !Array.isArray(snapshot.items) || snapshot.items.length > 256) return false;
  const names = new Set<string>();
  return snapshot.items.every(item => {
    if (!object(item) || typeof item.name !== "string" || !item.name || names.has(item.name) ||
      typeof item.pool !== "string" || !["FILESYSTEM", "VOLUME"].includes(String(item.type))) return false;
    names.add(item.name);
    return ["usedBytes", "availableBytes", "snapshotUsedBytes", "quotaBytes", "refQuotaBytes"].every(key =>
      item[key] === null || typeof item[key] === "number" && Number.isSafeInteger(item[key]) && (item[key] as number) >= 0);
  });
}
export function DatasetUsageView({ connectionId }: { connectionId: string }) {
  const { bytes, time } = useDisplayFormat();
  const id = useId();
  const [expanded, setExpanded] = useState(false);
  const [result, setResult] = useState<{ connectionId: string; value: State } | null>(null);
  const [error, setError] = useState<{ connectionId: string; message: string } | null>(null);
  const [loading, setLoading] = useState(false);
  const [revision, setRevision] = useState(0);
  const [now, setNow] = useState(Date.now());
  const [search, setSearch] = useState("");
  const [limit, setLimit] = useState(20);
  useEffect(() => {
    if (!expanded) return;
    const timer = setInterval(() => setNow(Date.now()), 10000);
    return () => clearInterval(timer);
  }, [expanded]);
  useEffect(() => {
    if (!expanded) return;
    const controller = new AbortController();
    const timeout = setTimeout(() => controller.abort("timeout"), 15000);
    setLoading(true);
    void fetch(`/api/v1/connections/${connectionId}/datasets`, { signal: controller.signal })
      .then(async response => {
        if (!response.ok) throw new Error("Unavailable");
        const value: unknown = await response.json();
        if (!valid(value)) throw new Error("Unsupported response");
        if (!controller.signal.aborted) { setResult({ connectionId, value }); setError(null); setNow(Date.now()); }
      }).catch(() => {
        if (!controller.signal.aborted || controller.signal.reason === "timeout")
          setError({ connectionId, message: "Could not refresh dataset usage. Any values below are from the last saved response." });
      }).finally(() => {
        clearTimeout(timeout);
        if (!controller.signal.aborted || controller.signal.reason === "timeout") setLoading(false);
      });
    return () => { clearTimeout(timeout); controller.abort(); };
  }, [connectionId, expanded, revision]);
  const state = result?.connectionId === connectionId ? result.value : null;
  const message = error?.connectionId === connectionId ? error.message : null;
  const recent = state?.enabled && state.status === "supported" && state.validUntilUtc !== null &&
    Date.parse(state.validUntilUtc) > now && state.snapshot !== null && Date.parse(state.snapshot.fetchedUtc) <= now;
  const items = state?.snapshot?.items ?? [];
  const matches = items.filter(item => item.name.toLocaleLowerCase().includes(search.trim().toLocaleLowerCase()));
  const quota = (value: number | null) => value === 0 ? "No quota set" : bytes(value);
  return <div style={{ marginTop: 16 }}>
    <Button variant="outline" aria-expanded={expanded} aria-controls={id} onClick={() => setExpanded(!expanded)}>
      {expanded ? "Hide dataset usage" : "View dataset usage"}
    </Button>
    {expanded && <section id={id} aria-label="Dataset space usage" aria-busy={loading} className="dataset-usage">
      <div className="section-title"><h3>Dataset space usage</h3>
        <Button variant="ghost" disabled={loading} onClick={() => setRevision(n => n + 1)}>Refresh dataset view</Button>
      </div>
      <p className="muted">See where space is used. Parent datasets include descendants; do not add these rows together or compare their sum with physical pool capacity.</p>
      {message && <p role="alert" className="error">{message}</p>}
      {!state && loading && <p role="status">Loading dataset usage…</p>}
      {state && <>
        <p className="dataset-status"><Badge variant="outline">{recent ? "Recent read" : state.status === "supported" ? "Last-known usage" : state.status}</Badge>
          <span className="muted">Fetched {state.snapshot ? time(state.snapshot.fetchedUtc) : "never"}</span></p>
        {!state.enabled && <p>Enable “Read dataset space usage” in <Link to="/settings">Settings</Link> to collect this optional inventory.</p>}
        {!recent && state.snapshot && <p className="muted">These saved values do not confirm current usage. Check collection coverage before making capacity decisions.</p>}
        {!state.snapshot ? <p>No saved dataset usage is available.</p> : <>
          {items.length === 0 ? <p>The API returned no visible datasets. This does not establish that the NAS contains no data.</p> : <>
            <div className="field"><Label htmlFor={`${id}-search`}>Find a dataset or volume</Label>
              <Input id={`${id}-search`} type="search" value={search} placeholder="Filter by name or pool"
                onChange={event => { setSearch(event.target.value); setLimit(20); }} /></div>
            <p className="muted" role="status">Showing {Math.min(limit, matches.length)} of {matches.length} matching entries · {items.length} saved</p>
            {matches.length === 0 && <p>No names match this filter. <Button variant="ghost" onClick={() => setSearch("")}>Clear filter</Button></p>}
            <div className="dataset-list">{matches.slice(0, limit).map(item => <Card className="dataset-card" key={item.name}>
              <div className="section-title"><h4>{item.name}</h4><Badge variant="outline">{item.type === "VOLUME" ? "Volume" : "Filesystem"}</Badge></div>
              <dl className="dataset-metrics">
                <div><dt>Used, including descendants</dt><dd>{bytes(item.usedBytes)}</dd></div>
                <div><dt>Available to this dataset</dt><dd>{bytes(item.availableBytes)}</dd></div>
                <div><dt>Used by its snapshots</dt><dd>{bytes(item.snapshotUsedBytes)}</dd></div>
              </dl>
              <details><summary>Quota details</summary><dl className="dataset-metrics">
                <div><dt>Dataset quota</dt><dd>{quota(item.quotaBytes)}</dd></div>
                <div><dt>Referenced-data quota</dt><dd>{quota(item.refQuotaBytes)}</dd></div>
              </dl></details>
            </Card>)}</div>
            {matches.length > limit && <Button variant="outline" onClick={() => setLimit(n => n + 20)}>Show more datasets</Button>}
          </>}
          <details style={{ marginTop: 16 }}><summary>How to interpret this inventory</summary>
            <p className="muted">Available space can reflect quotas and shared pool limits. Snapshot usage is already included in used space. Unknown values are not zero. Dataset names can change or be reused; this view does not infer renames, permanent identity, drive health, or recovery coverage.</p>
            <p className="muted">This view shows the latest snapshot, up to 256 entries visible to the NAS account. Sampled historical inventories are available below. The NAS API excludes internal datasets. No files are scanned and no dataset settings are changed. Source: {state.snapshot.sourceSchema}.</p>
          </details>
        </>}
      </>}
      <DatasetHistoryView key={connectionId} connectionId={connectionId}/>
    </section>}
  </div>;
}
