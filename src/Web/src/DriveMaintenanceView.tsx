import { SelectField, SelectOption } from "@/components/SelectField";
import { useEffect, useId, useRef, useState } from "react";
import { Button } from "@/components/ui/button";
import { Card } from "@/components/ui/card";
import { Badge } from "@/components/ui/badge";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { useDisplayFormat } from "./displayPreferences";

type Entry = { id: number; diskId: string; occurredUtc: string; dateConfidence: string; kind: string; outcome: string;
  summary: string; evidence: string; confirmedIdentity: string; author: string; recordedUtc: string;
  voidedUtc: string | null; voidReason: string | null; voidedBy: string | null };
type Page = { items: Entry[]; nextBefore: number | null };
function valid(value: unknown): value is Page {
  if (!value || typeof value !== "object") return false;
  const page = value as Page;
  return Array.isArray(page.items) && page.items.length <= 25 && (page.nextBefore === null || Number.isSafeInteger(page.nextBefore) && page.nextBefore > 0) &&
    page.items.every(item => item && Number.isSafeInteger(item.id) && item.id > 0 &&
      [item.diskId,item.dateConfidence,item.kind,item.outcome,item.summary,item.evidence,item.confirmedIdentity,item.author].every(x => typeof x === "string") &&
      [item.occurredUtc,item.recordedUtc].every(x => typeof x === "string" && Number.isFinite(Date.parse(x))) &&
      (item.voidedUtc === null || typeof item.voidedUtc === "string" && Number.isFinite(Date.parse(item.voidedUtc))) &&
      [item.voidReason,item.voidedBy].every(x => x === null || typeof x === "string"));
}
export function DriveMaintenanceView({disk, days}: {disk: {id: string; serial: string; wwn: string | null; mappingConfidence: string}; days: number}) {
  const {time} = useDisplayFormat(); const id = useId();
  const [pagination,setPagination] = useState<{days:number;before:number | null}>({days,before:null});
  const setBefore = (before:number | null) => setPagination({days,before});
  const [revision,setRevision] = useState(0);
  const [result,setResult] = useState<{key:string;page:Page} | null>(null);
  const [error,setError] = useState<{key:string;text:string} | null>(null);
  const [message,setMessage] = useState("");
  const [busy,setBusy] = useState(false);
  const [showForm,setShowForm] = useState(false);
  const requestId = useRef("");
  const messageRef = useRef<HTMLParagraphElement>(null);
  useEffect(() => { if (message) messageRef.current?.focus(); }, [message]);
  // Scope a cursor to the selected trend window without briefly requesting its old cursor.
  const cursor = pagination.days === days ? pagination.before : null;
  const key = `${disk.id}:${days}:${cursor}`;
  useEffect(() => {
    const controller = new AbortController();
    const timer = setTimeout(() => controller.abort("timeout"),15000);
    void fetch(`/api/v1/disks/${disk.id}/maintenance?days=${days}${cursor ? `&before=${cursor}` : ""}`,{signal:controller.signal})
      .then(async response => { if (!response.ok) throw new Error(); const page: unknown = await response.json();
        if (!valid(page)) throw new Error();
        if (!controller.signal.aborted) { setResult({key,page}); setError(null); }
      }).catch(() => { if (!controller.signal.aborted || controller.signal.reason === "timeout") setError({key,text:"Could not refresh maintenance history. Any entries below are from the last saved response for this window."}); })
      .finally(() => clearTimeout(timer));
    return () => { clearTimeout(timer); controller.abort(); };
  },[disk.id,days,cursor,key,revision]);
  const page = result?.key === key ? result.page : null;
  const identity = disk.serial || disk.wwn || "";
  const canRecord = !!identity && disk.mappingConfidence !== "ambiguous";
  async function write(path: string, body: unknown) {
    setBusy(true); setMessage("");
    try {
      const response = await fetch(`/api/v1/disks/${disk.id}/maintenance${path}`,{method:"POST",headers:{"Content-Type":"application/json","X-Homelab-Request":"1"},body:JSON.stringify(body),signal:AbortSignal.timeout(15000)});
      if (!response.ok) { const detail = await response.json().catch(() => ({})); throw new Error(detail.error || "Could not save this record."); }
      setShowForm(false); setBefore(null); setRevision(n => n + 1);
      setMessage("History saved. The selected trend window still applies. Drive findings and counter baselines are unchanged.");
    } catch (error) { setMessage(error instanceof Error && error.name !== "TimeoutError" && error.name !== "TypeError" ? error.message : "Save outcome is unknown. Refresh history before recording another event; retrying the same unchanged form is safe."); }
    finally { setBusy(false); }
  }
  return <section aria-label="Drive maintenance history" className="drive-maintenance">
    <div className="section-title"><h3>Maintenance context</h3><Button variant="outline" disabled={!canRecord || busy || showForm} onClick={() => {requestId.current = crypto.randomUUID();setShowForm(true);setMessage("");}}>Record past work</Button></div>
    <p className="muted">Manual records alongside the selected {days ? `${days}-day` : "all-history"} trend window. Recording work does not run NAS commands, prove a repair, resolve findings, or reset counters. Use replacement history for drive replacements.</p>
    {!canRecord && <p className="muted">Resolve the drive’s missing or ambiguous identity before attaching physical maintenance records.</p>}
    {message && <p role="status" tabIndex={-1} ref={messageRef} className="maintenance-message">{message}</p>}
    {showForm && <Card className="maintenance-form"><form onSubmit={event => {
      event.preventDefault(); const fields = Object.fromEntries(new FormData(event.currentTarget));
      const occurred = new Date(String(fields.occurredUtc)+"Z");
      if (!Number.isFinite(occurred.getTime())) { setMessage("Enter a valid past UTC time for the recorded work."); return; }
      void write("",{...fields,requestId:requestId.current,occurredUtc:occurred.toISOString()});
    }}>
      <h4>Record work performed outside Homelab Control</h4>
      <p className="muted">Drive identity: {identity}. Bay verification remains separate. Describe observed work and evidence, without assuming it fixed the problem.</p>
      <div className="field"><Label htmlFor={`${id}-kind`}>Work category</Label><SelectField id={`${id}-kind`} name="kind"><SelectOption value="inspection">Inspection</SelectOption><SelectOption value="cabling">Cabling or connection work</SelectOption><SelectOption value="firmware">Firmware work</SelectOption><SelectOption value="relocation">Physical relocation</SelectOption><SelectOption value="other">Other maintenance</SelectOption></SelectField></div>
      <div className="field"><Label htmlFor={`${id}-time`}>Work time (UTC)</Label><Input id={`${id}-time`} name="occurredUtc" type="datetime-local" required /></div>
      <div className="field"><Label htmlFor={`${id}-confidence`}>Date confidence</Label><SelectField id={`${id}-confidence`} name="dateConfidence"><SelectOption value="documented">Documented</SelectOption><SelectOption value="estimated">Estimated</SelectOption></SelectField></div>
      <div className="field"><Label htmlFor={`${id}-outcome`}>Work outcome</Label><SelectField id={`${id}-outcome`} name="outcome" defaultValue="unknown"><SelectOption value="unknown">Unknown</SelectOption><SelectOption value="completed">Completed — not a health verdict</SelectOption><SelectOption value="incomplete">Incomplete</SelectOption></SelectField></div>
      <div className="field"><Label htmlFor={`${id}-summary`}>Work summary</Label><Input id={`${id}-summary`} name="summary" maxLength={2000} required /></div>
      <div className="field"><Label htmlFor={`${id}-evidence`}>Maintenance evidence</Label><textarea id={`${id}-evidence`} name="evidence" maxLength={4000} rows={3} required /></div>
      <div className="field"><Label htmlFor={`${id}-identity`}>Confirm drive serial or WWN</Label><Input id={`${id}-identity`} name="confirmedIdentity" autoComplete="off" required /></div>
      <div className="review-actions"><Button disabled={busy}>Save maintenance record</Button><Button type="button" variant="ghost" disabled={busy} onClick={() => setShowForm(false)}>Cancel maintenance record</Button></div>
    </form></Card>}
    {error?.key === key && <p role="alert" className="error">{error.text}</p>}
    <Button variant="ghost" disabled={busy} onClick={() => setRevision(n => n + 1)}>Refresh maintenance history</Button>
    {!page && error?.key !== key && <p className="muted">Loading maintenance records…</p>}
    {page?.items.length === 0 && <p className="muted">No maintenance records in this window. This does not establish that no work occurred.</p>}
    {page?.items.map(entry => <Card key={entry.id} className="maintenance-entry">
      <div className="section-title"><h4>{entry.summary}</h4><Badge variant="outline">{entry.voidedUtc ? "Marked incorrect" : entry.outcome}</Badge></div>
      <p>{entry.kind} · {time(entry.occurredUtc)} · {entry.dateConfidence} date</p>
      <p className="evidence-text">{entry.evidence}</p>
      <small>Recorded by {entry.author} at {time(entry.recordedUtc)} · Confirmed identity: {entry.confirmedIdentity}</small>
      {entry.voidedUtc ? <p className="muted">Marked incorrect by {entry.voidedBy} at {time(entry.voidedUtc)}: {entry.voidReason}. Original evidence retained.</p> : <details><summary>Correct this record</summary><p className="muted">The original entry remains visible. Mark it incorrect with a reason, then record corrected details separately.</p><form onSubmit={event => {event.preventDefault();void write(`/${entry.id}/void`,{reason:new FormData(event.currentTarget).get("reason")});}}>
        <Label htmlFor={`${id}-void-${entry.id}`}>Reason this record is incorrect</Label><Input id={`${id}-void-${entry.id}`} name="reason" maxLength={2000} required /><Button variant="outline" disabled={busy}>Mark record incorrect</Button>
      </form></details>}
    </Card>)}
    {page && <div className="review-actions"><small>Newest recorded first · up to 25 per page</small><div><Button variant="ghost" disabled={cursor === null} onClick={() => setBefore(null)}>Latest maintenance</Button><Button variant="outline" disabled={page.nextBefore === null} onClick={() => setBefore(page.nextBefore)}>Older maintenance</Button></div></div>}
  </section>;
}
