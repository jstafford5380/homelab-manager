import {useEffect,useState} from "react";
import {Button} from "@/components/ui/button";
import {useDisplayFormat} from "../../../lib/displayPreferences";
import {ScanTiming} from "../ScanTiming";
import {validScanTiming,type ScanTimingFields} from "../scanModel";
type Scan = ScanTimingFields & { id: number; scanFunction: string; scanState: string; scanErrors: number | null; scanErrorsPeak?: number | null; scanPercentage: number | null; firstFetchedUtc: string; lastFetchedUtc: string; sourceSchema: string };
type Page = { items: Scan[]; nextBefore: number | null };
export function PoolScanHistoryView({ connectionId, guid }: { connectionId: string; guid: string }) {
  const { time } = useDisplayFormat();
  const [expanded, setExpanded] = useState(false), [before, setBefore] = useState<number | null>(null), [retry, setRetry] = useState(0);
  const [result, setResult] = useState<{ key: string; page: Page } | null>(null);
  const [failure, setFailure] = useState<{ key: string; message: string } | null>(null);
  const key = `${connectionId}/${guid}/${before ?? "latest"}`;
  const page = result?.key === key ? result.page : null;
  const error = failure?.key === key ? failure.message : "";
  useEffect(() => {
    if (!expanded) return;
    const controller = new AbortController();
    const timer = setTimeout(() => controller.abort("timeout"), 15000);
    void fetch(`/api/v1/connections/${connectionId}/pools/${encodeURIComponent(guid)}/scans${before === null ? "" : `?before=${before}`}`, { signal: controller.signal })
      .then(async response => {
        if (!response.ok) throw new Error();
        const data: Page = await response.json();
        if (!data || !Array.isArray(data.items) || data.items.some(row => !row || !Number.isSafeInteger(row.id) || row.id <= 0 ||
          !validScanTiming(row) || typeof row.scanStartedUtc !== "string" ||
          ![row.scanFunction,row.scanState,row.sourceSchema].every(v => typeof v === "string") ||
          ![row.firstFetchedUtc,row.lastFetchedUtc].every(v => typeof v === "string" && Number.isFinite(Date.parse(v))) ||
          !(row.scanErrorsPeak == null || Number.isSafeInteger(row.scanErrorsPeak) && row.scanErrorsPeak >= 0) ||
          !(row.scanErrors === null || Number.isSafeInteger(row.scanErrors) && row.scanErrors >= 0) ||
          !(row.scanPercentage === null || typeof row.scanPercentage === "number" && row.scanPercentage >= 0 && row.scanPercentage <= 100)) ||
          !(data.nextBefore === null || Number.isSafeInteger(data.nextBefore) && data.nextBefore > 0)) throw new Error();
        if (!controller.signal.aborted) { setResult({key,page:data}); setFailure(null); }
      }).catch(() => {
        if (!controller.signal.aborted || controller.signal.reason === "timeout")
          setFailure({key,message:"Scan history could not be loaded. Any displayed entries are from the last successful read."});
      }).finally(() => clearTimeout(timer));
    return () => { clearTimeout(timer); controller.abort(); };
  }, [connectionId,guid,before,key,expanded,retry]);
  return <div style={{marginTop:16}}>
    <Button variant="outline" aria-expanded={expanded} onClick={() => { setExpanded(!expanded); setBefore(null); }}>
      {expanded ? "Hide scan history" : "View scrub & resilver history"}
    </Button>
    {expanded && <section aria-label="Recorded scan history" style={{marginTop:16}}>
      <p className="muted">Grouped by NAS, pool, scan type and reported start time; newest recorded groups first. Entries describe the latest retained source report, not current operation state or verified data integrity. Scans without a known start time remain only in snapshot history. Groups are retained for two years after their last report; cleanup requires an application backup.</p>
      <div style={{display:"flex",gap:8,flexWrap:"wrap",marginBlock:12}}>
        <Button variant="outline" onClick={() => setRetry(value => value+1)}>Refresh scan history</Button>
        {before !== null && <Button variant="outline" onClick={() => setBefore(null)}>Latest scan groups</Button>}
      </div>
      {error && <p role="alert">{error}</p>}
      {!page && !error && <p role="status">Loading scan history…</p>}
      {page?.items.length === 0 && <p>No source-identified scans recorded on this page. This does not prove that scans ran successfully or were never needed.</p>}
      {page?.items.map(scan => <article className="evidence-coverage-item" key={scan.id} style={{marginBlock:12}}>
        <h4>{scan.scanFunction} · {scan.scanPausedUtc ? "PAUSED (reported)" : scan.scanState}</h4>
        <p>Last reported progress: {scan.scanPercentage === null ? "Unknown" : `${scan.scanPercentage.toFixed(1)}%`} · Scan errors: {scan.scanErrors ?? "Unknown"}</p>
        <p>Highest retained reported errors: {scan.scanErrorsPeak ?? "Unknown"}</p>
        {(scan.scanErrorsPeak ?? 0) > 0 && <p className="muted">Earlier errors remain evidence even if the latest count is lower or unknown. Review the associated action item; this does not by itself establish permanent data loss.</p>}
        <p className="muted">Last source read: {time(scan.lastFetchedUtc)}</p>
        <ScanTiming scan={scan} />
        <small>First seen: {time(scan.firstFetchedUtc)} · Source schema: {scan.sourceSchema}</small>
      </article>)}
      {page?.nextBefore != null && <Button variant="outline" onClick={() => setBefore(page.nextBefore)}>Older scan groups</Button>}
    </section>}
  </div>;
}
