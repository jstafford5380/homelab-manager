import {SelectField,SelectOption} from "@/components/SelectField";
import {Link} from "react-router";
import {useId,useState} from "react";
import {Button} from "@/components/ui/button";
import {Card} from "@/components/ui/card";

type DriveMapping = { status: string; diskId: string | null; serial: string | null; wwn: string | null; model: string | null; explanation: string };
type Node = { driveMapping?: DriveMapping | null; guid: string; parentGuid: string | null; role: string; type: string; state: string | null; path: string | null; device: string | null; diskAlias: string | null; readErrors: number | null; writeErrors: number | null; checksumErrors: number | null };
type Range = { knownCount: number; min: number | null; max: number | null };
type Row = { node: Node; read: Range; write: Range; checksum: Range };
export type VdevComparison = {status: string; explanation: string; previousFetchedUtc: string | null; members: {guid: string; read: {status: string; difference: number | null}; write: {status: string; difference: number | null}; checksum: {status: string; difference: number | null}}[]};
type Evidence = { status: string; explanation: string; samples: number; daily: boolean; rows: Row[] };
const count = (v: unknown): v is number => typeof v === "number" && Number.isSafeInteger(v) && v >= 0;
const optionalCount = (v: unknown) => v === null || count(v);
function parse(json: string): Evidence {
  if (typeof json !== "string" || json.length > 512 * 1024) throw Error();
  const data = JSON.parse(json);
  const daily = data.schema === "truenas-24.10-vdev-daily-v1";
  if ((!daily && data.schema !== "truenas-24.10-vdev-evidence-v1") || !Array.isArray(data.nodes) || data.nodes.length > 4096 || typeof data.explanation !== "string" || !["reported", "unavailable"].includes(data.status)) throw Error();
  const samples = daily ? data.samples : 1;
  if (!count(samples) || samples < 1) throw Error();
  const rows: Row[] = data.nodes.map((item: Node | Row) => {
    const node = daily ? (item as Row).node : item as Node;
    if (!node || ![node.guid, node.role, node.type].every(v => typeof v === "string" && v.length > 0) || ![node.parentGuid, node.state, node.path, node.device, node.diskAlias].every(v => v === null || typeof v === "string")) throw Error();
    const range = (value: number | null): Range => ({knownCount: value === null ? 0 : 1, min: value, max: value});
    const row = daily ? item as Row : {node, read: range(node.readErrors), write: range(node.writeErrors), checksum: range(node.checksumErrors)};
    if (![row.read, row.write, row.checksum].every(r => r && count(r.knownCount) && r.knownCount <= samples && optionalCount(r.min) && optionalCount(r.max) && (r.knownCount === 0 ? r.min === null && r.max === null : r.min !== null && r.max !== null && r.min <= r.max))) throw Error();
    return row;
  });
  return {status: data.status, explanation: data.explanation, samples, daily, rows};
}
function validMapping(value: DriveMapping | null | undefined): value is DriveMapping {
  return !!value && ["inferred","unavailable","ambiguous","not_applicable"].includes(value.status) && typeof value.explanation === "string" && [value.serial,value.wwn,value.model].every(x => x === null || typeof x === "string") && (value.diskId === null || typeof value.diskId === "string" && /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(value.diskId));
}
function validComparison(value: VdevComparison | undefined): value is VdevComparison {
  return !!value && typeof value.explanation === "string" && typeof value.status === "string" && Array.isArray(value.members) && value.members.length <= 4096 && value.members.every(m => m && typeof m.guid === "string" && [m.read,m.write,m.checksum].every(c => c && typeof c.status === "string" && optionalCount(c.difference)));
}
export default function VdevEvidenceView({entries}: {entries: {id: string; label: string; json?: string | null; comparison?: VdevComparison}[]}) {
  const id = useId();
  const [selected, setSelected] = useState("");
  const [limit, setLimit] = useState(20);
  const entry = entries.find(e => e.id === selected) ?? entries[0];
  const comparison = validComparison(entry?.comparison) ? entry.comparison : null;
  let evidence: Evidence | null = null;
  let invalid = false;
  if (entry?.json != null) { try { evidence = parse(entry.json); } catch { invalid = true; } }
  return <details className="vdev-evidence">
    <summary>Member error history</summary>
    <p className="muted">Historical ZFS members. These identifiers and aliases are not verified physical drive or bay mappings. Counts do not predict a failure date.</p>
    {entries.length > 1 && <div><label htmlFor={id}>Saved snapshot</label><SelectField id={id} value={entry?.id ?? ""} onValueChange={e => {setSelected(e); setLimit(20);}}>{entries.map(e => <SelectOption key={e.id} value={e.id}>{e.label}</SelectOption>)}</SelectField></div>}
    {!evidence && <p>{invalid ? "Member evidence could not be read safely." : "Member counters were not retained for this snapshot."}</p>}
    {evidence && <>
      <p className="muted">{evidence.explanation}</p>
      {comparison && <div className="vdev-comparison"><strong>Comparison with preceding reading</strong><p>{comparison.explanation}</p></div>}
      {evidence.status === "unavailable" ? <p>Member evidence unavailable.</p> : evidence.rows.length === 0 ? <p>No members were reported.</p> : <>
        <p>{evidence.rows.length} reported members · {evidence.daily ? `${evidence.samples} readings; ranges are not changes in errors` : "One saved reading"}</p>
        <div className="vdev-member-list">{evidence.rows.slice(0,limit).map(row => <Card key={row.node.guid} className="daily-summary">
          <h4>{row.node.role} · {row.node.type} · {row.node.state || "State unknown"}</h4>
          <p>Member ID: <span className="vdev-identifier">{row.node.guid}</span><br/>Parent: <span className="vdev-identifier">{row.node.parentGuid || "Top-level member"}</span></p>
          <dl className="daily-summary-metrics">{([["Read errors", row.read], ["Write errors", row.write], ["Checksum errors", row.checksum]] as const).map(([label,r]) => <div key={label}><dt>{label}</dt><dd>{r.min === null ? "Unknown" : r.min === r.max ? r.min.toLocaleString() : `${r.min.toLocaleString()} – ${r.max!.toLocaleString()}`}{evidence.daily && <small>{r.knownCount} known · {evidence.samples-r.knownCount} unknown readings</small>}</dd></div>)}</dl>
          {comparison?.status === "compared" && (() => {
            const change = comparison.members.find(m => m.guid === row.node.guid);
            if (!change) return null;
            return <p>Observed differences: {([["read",change.read],["write",change.write],["checksum",change.checksum]] as const).map(([label,value]) => `${label} ${value.difference === null ? "unknown" : `+${value.difference.toLocaleString()}`}`).join(" · ")}</p>;
          })()}
          {validMapping(row.node.driveMapping) && row.node.driveMapping.status !== "not_applicable" && <div>
            <strong>Drive match: {row.node.driveMapping.status}</strong>
            <p className="muted">{row.node.driveMapping.explanation}</p>
            {row.node.driveMapping.status === "inferred" && row.node.driveMapping.diskId && <>
              <p className="vdev-identifier">{row.node.driveMapping.model || "Model unknown"} · Serial: {row.node.driveMapping.serial || "Unknown"}<br/>WWN: {row.node.driveMapping.wwn || "Unknown"}</p>
              <Button asChild variant="outline"><Link to={`/storage?disk=${encodeURIComponent(row.node.driveMapping.diskId)}`}>View matched drive evidence</Link></Button>
            </>}
          </div>}
          {(row.node.path || row.node.device || row.node.diskAlias) && <details><summary>Reported aliases</summary><p className="vdev-identifier">Path: {row.node.path || "Unknown"}<br/>Device: {row.node.device || "Unknown"}<br/>Disk alias: {row.node.diskAlias || "Unknown"}</p></details>}
        </Card>)}</div>
        {limit < evidence.rows.length && <Button variant="outline" onClick={() => setLimit(v => v+20)}>Show next 20 members</Button>}
      </>}
    </>}
  </details>;
}
