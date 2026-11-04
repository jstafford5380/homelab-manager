import { SelectField, SelectOption } from "@/components/SelectField";
import { useEffect, useRef, useState } from "react";
import { Button } from "@/components/ui/button";
import { useDisplayFormat } from "./displayPreferences";

type Item = {id:string;policyRevision:string;occurredUtc:string;recordedUtc:string;author:string;outcome:string;reviewByUtc:string;recoveryPointUtc:string|null;destinationConfirmed:boolean|null;scope:string|null;checks:string|null;durationMinutes:number|null;evidence:string;failureReason:string|null};
type Page = {asOfUtc:string;kind:string;items:Item[];next:{atUtc:string;id:string}|null};
export function RecoveryHistory({policyId,revision,asset}:{policyId:string;revision:string;asset:string}) {
 const [open,setOpen]=useState(false);const [kind,setKind]=useState('runs');
 return <section aria-label={`Recovery history for ${asset}`}>
  <Button variant="outline" aria-expanded={open} onClick={()=>setOpen(value=>!value)}>{open?'Hide full recovery history':'Browse all recovery evidence'}</Button>
  {open && <><label style={{display:'block',marginTop:16}}>Evidence type <SelectField aria-label={`Evidence type for ${asset}`} value={kind} onValueChange={event=>setKind(event)}><SelectOption value="runs">Backup runs</SelectOption><SelectOption value="exercises">Restore exercises</SelectOption></SelectField></label>
  <HistoryPage key={`${policyId}:${revision}:${kind}`} policyId={policyId} revision={revision} kind={kind}/></>}
 </section>;
}
function HistoryPage({policyId,revision,kind}:{policyId:string;revision:string;kind:string}) {
 const {time}=useDisplayFormat();const [page,setPage]=useState<Page|null>(null);const [error,setError]=useState('');const [busy,setBusy]=useState(true);const lifetime=useRef<AbortController|null>(null);
 async function read(previous:Page|null,signal:AbortSignal) {
  const query=new URLSearchParams({kind});
  if(previous?.next){query.set('asOfUtc',previous.asOfUtc);query.set('beforeAtUtc',previous.next.atUtc);query.set('beforeId',previous.next.id);}
  const response=await fetch(`/api/v1/recovery/${policyId}/history?${query}`,{signal:AbortSignal.any([signal,AbortSignal.timeout(15000)])});
  if(!response.ok)throw new Error(`Recovery history could not be loaded (${response.status}).`);
  const result=await response.json() as Page;
  if(!Array.isArray(result.items))throw new Error('Recovery history response was incomplete.');
  if(!signal.aborted)setPage(result);
 }
 useEffect(()=>{const controller=new AbortController();lifetime.current=controller;
  void read(null,controller.signal).catch(error=>{if(!controller.signal.aborted)setError(error instanceof Error?error.message:'History unavailable.');}).finally(()=>{if(!controller.signal.aborted)setBusy(false);});
  return ()=>controller.abort();
 // The parent keys this component by policy, revision and evidence type.
 // eslint-disable-next-line react-hooks/exhaustive-deps
 },[]);
 async function load(previous:Page|null){if(busy||!lifetime.current)return;const signal=lifetime.current.signal;setBusy(true);setError('');
  try{await read(previous,signal);}catch(error){if(!signal.aborted)setError(error instanceof Error?error.message:'History unavailable.');}finally{if(!signal.aborted)setBusy(false);}}
 return <div>
  {error && <p role="alert">{error} {page?'Previously loaded records remain visible.':''}</p>}
  <p role="status" className="muted">{busy?'Loading recovery history…':page?`${page.items.length} records on this page. ${page.next?'Older records are available.':'End of retained history for this view.'}`:'History unavailable.'}</p>
  {page && <p className="muted">Includes records submitted through {time(page.asOfUtc)}. Refresh to include later submissions. Historical evidence does not establish current recovery readiness.</p>}
  {page?.items.map(row=><article className="daily-summary" key={row.id}>
   <h3>{kind==='runs'?'Backup':'Restore'} {row.outcome} · {time(row.occurredUtc)}</h3>
   <p className="muted">{row.policyRevision.toLowerCase()===revision.toLowerCase()?'Current policy revision':'Earlier policy revision — current scope may differ.'}</p>
   <p>Represented recovery point: {time(row.recoveryPointUtc)}</p>
   {row.destinationConfirmed!==null && <p>Recorded destination confirmation: {row.destinationConfirmed?'yes':'no'}</p>}
   {row.scope!==null && <p>Restored scope: {row.scope}</p>}
   {row.durationMinutes!==null && <p>Measured duration: {row.durationMinutes} minutes · Checks: {row.checks||'Not recorded'}</p>}
   <p style={{whiteSpace:'pre-wrap'}}>{row.evidence||'Evidence not recorded'}</p>{row.failureReason && <p>Failure reason: {row.failureReason}</p>}
   <small>{row.author||'Unknown author'} · Recorded {time(row.recordedUtc)} · Review by {time(row.reviewByUtc)}</small>
  </article>)}
  <div className="review-actions">{page?.next && <Button variant="outline" disabled={busy} onClick={()=>void load(page)}>Older evidence</Button>}<Button variant="outline" disabled={busy} onClick={()=>void load(null)}>{page?'Back to latest evidence':'Retry recovery history'}</Button></div>
 </div>;
}
