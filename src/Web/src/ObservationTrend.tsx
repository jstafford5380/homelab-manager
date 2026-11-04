import { HoverTip } from "@/components/HoverTip";
import { SelectField, SelectOption } from "@/components/SelectField";
import { useEffect, useRef, useState } from "react";
import { Card } from "@/components/ui/card";
import { Button } from "@/components/ui/button";

type Observation = {id:number;metric:string;value:number|null;unit:string;observedUtc:string|null;receivedUtc:string;recordedBy:string|null;source:string;quality:string;missingReason:string|null};
type Change = {previousId:number|null;previousObservedUtc:string|null;status:string;difference:number|null;unit:string;explanation:string};
type History = {asOfUtc:string;snapshotId:number;items:Observation[];nextBefore:number|null;comparisons?:Record<string,Change>};
export function ObservationTrend({diskId,metric,metrics,setMetric,days,setDays,timezone}: {
 diskId:string;metric:string;metrics:string[];setMetric:(value:string)=>void;days:number;setDays:(value:number)=>void;timezone:string;
}) {
 const [history,setHistory]=useState<History|null>(null);
 const [busy,setBusy]=useState(false);const [error,setError]=useState("");
 const controller=useRef<AbortController|null>(null);
 const stamp=(value:string|null)=>value?new Date(value).toLocaleString(undefined,{timeZone:timezone}):"Unknown";
 async function read(previous:History|null,signal:AbortSignal) {
  const query=new URLSearchParams({metric,days:String(days)});
  if(previous?.nextBefore){query.set('before',String(previous.nextBefore));query.set('snapshotId',String(previous.snapshotId));query.set('asOfUtc',previous.asOfUtc);}
  const response=await fetch(`/api/v1/disks/${diskId}/observation-history?${query}`,{signal:AbortSignal.any([signal,AbortSignal.timeout(15000)])});
  if(!response.ok)throw new Error(`Observation history could not be loaded (${response.status}).`);
  const page=await response.json() as History;
  if(signal.aborted)return;
  const append=previous && previous.items.length<1000;
  setHistory({...page,items:append?[...previous.items,...page.items]:page.items,comparisons:append?{...previous.comparisons,...page.comparisons}:page.comparisons});
 }
 useEffect(()=>{
  const active=new AbortController();controller.current=active;setBusy(true);
  void read(null,active.signal).catch(error=>{if(!active.signal.aborted)setError(error instanceof Error?error.message:"History could not be loaded.");}).finally(()=>{if(!active.signal.aborted)setBusy(false);});
  return ()=>active.abort();
 // This component is keyed by drive, metric, window and refresh revision by its parent.
 // eslint-disable-next-line react-hooks/exhaustive-deps
 },[]);
 async function load(previous:History|null) {
  if(!controller.current || busy)return;
  setBusy(true);setError("");
  try{await read(previous,controller.current.signal);}catch(error){if(!controller.current.signal.aborted)setError(error instanceof Error?error.message:"History could not be loaded.");}
  finally{if(!controller.current.signal.aborted)setBusy(false);}
 }
 const points=(history?.items??[]).filter(x=>x.observedUtc).sort((a,b)=>Date.parse(a.observedUtc!)-Date.parse(b.observedUtc!)||a.id-b.id);
 const undated=(history?.items??[]).filter(x=>!x.observedUtc);
 const valid=points.filter(x=>x.value!==null && Number.isFinite(x.value) && x.quality==='observed');
 const end=history?Date.parse(history.asOfUtc):Date.now();
 const start=days?end-days*86400000:Math.min(...valid.map(x=>Date.parse(x.observedUtc!)),end-86400000);
 const low=Math.min(0,...valid.map(x=>x.value!));const high=Math.max(1,...valid.map(x=>x.value!));
 return <Card className="list-card">
  <h2 style={{marginTop:16}}>Observed trends</h2>
  <div className="row"><SelectField aria-label="Metric" value={metric} onValueChange={event=>setMetric(event)}>{metrics.map(value=><SelectOption key={value}>{value}</SelectOption>)}</SelectField>
   <SelectField aria-label="History window" value={days} onValueChange={event=>setDays(Number(event))}><SelectOption value={1}>24 hours</SelectOption><SelectOption value={7}>7 days</SelectOption><SelectOption value={30}>30 days</SelectOption><SelectOption value={0}>All history</SelectOption></SelectField></div>
  {error && <p role="alert" className="error">{error} {history?'Previously loaded evidence remains below.':''}</p>}
  <p role="status" className="muted">{busy?'Loading observation history…':history?`${history.items.length} records loaded for this metric. ${history.nextBefore?'More retained evidence is available.':'End of retained history for this window.'}`:'History unavailable.'}</p>
  {history && <p className="muted">Window ends {stamp(history.asOfUtc)}. Records load by import order; older imports may contain newer observation times. The chart covers loaded records only. Dashboard refreshes preserve this history snapshot; use Refresh history for new imports.</p>}
  {valid.length>0?<svg viewBox="0 0 600 190" role="img" aria-label={`${metric} observations. Separate points preserve collection gaps. Loaded records only.`}>
   <line x1="45" y1="160" x2="580" y2="160" stroke="var(--chart-axis)"/><line x1="45" y1="15" x2="45" y2="160" stroke="var(--chart-axis)"/>
   <text x="3" y="20" fontSize="10">{high}</text><text x="3" y="160" fontSize="10">{low}</text>
   {valid.map(row=><HoverTip key={row.id} content={`${stamp(row.observedUtc)}: ${row.value} ${row.unit} · ${row.source}`}><circle tabIndex={0} cx={45+(Date.parse(row.observedUtc!)-start)/Math.max(1,end-start)*535} cy={160-(row.value!-low)/(high-low)*135} r="4" fill="var(--chart-point)"></circle></HoverTip>)}
  </svg>:history && <p>No timestamped, observed numeric readings in the loaded records.</p>}
  <p className="muted">Points show recorded evidence, not continuous coverage. Counter decreases may reflect resets or source changes; age and SMART passing do not predict remaining life.</p>
  <div style={{maxHeight:300,overflow:'auto'}}>{points.map(row=><div className="row" key={row.id}><div><h3>{row.value??'Unknown'} {row.unit}</h3><small>{stamp(row.observedUtc)} · {row.source} · {row.quality}<br/>Imported by {row.recordedBy||'Unknown (legacy record)'} at {stamp(row.receivedUtc)}</small>{row.missingReason && <p>{row.missingReason}</p>}<details><summary>Change evidence</summary>{history?.comparisons?.[row.id]?<><p>{history.comparisons[row.id].difference !== null ? `${history.comparisons[row.id].difference! > 0 ? "+" : ""}${history.comparisons[row.id].difference} ${history.comparisons[row.id].unit} since ${stamp(history.comparisons[row.id].previousObservedUtc)}` : "No numeric comparison"}</p><p className="muted">{history.comparisons[row.id].explanation}</p>{history.comparisons[row.id].previousId !== null && <small>Previous retained observation #{history.comparisons[row.id].previousId} · {stamp(history.comparisons[row.id].previousObservedUtc)}</small>}</>:<p className="muted">Comparison evidence is unavailable. Refresh history after the service is updated.</p>}</details></div></div>)}</div>
  {undated.length>0 && <details><summary>Evidence without observation time ({undated.length})</summary><p className="muted">These records are retained for review but are not plotted or assigned to the selected time window.</p>{undated.map(row=><div className="row" key={row.id}><div><strong>{row.value??'Unknown'} {row.unit}</strong><p>{row.source} · {row.quality}</p><small>Observation time unknown · Imported {stamp(row.receivedUtc)} by {row.recordedBy||'Unknown (legacy record)'}</small>{row.missingReason && <p>{row.missingReason}</p>}</div></div>)}</details>}
  {history?.nextBefore && history.items.length>=1000 && <p className="muted">To keep this view responsive, the next page replaces these 1,000 records. Refresh history to return to the latest imports.</p>}
  <div className="review-actions">{history?.nextBefore && <Button variant="outline" disabled={busy} onClick={()=>void load(history)}>{history.items.length>=1000?'View next older page':'Load more observations'}</Button>}
   <Button variant="outline" disabled={busy} onClick={()=>void load(null)}>{history?'Refresh history':'Retry history'}</Button></div>
 </Card>;
}
