import { SelectField, SelectOption } from "@/components/SelectField";
import { DatasetRenameView } from "./DatasetRenameView";
import { useEffect, useId, useRef, useState } from "react";
import { Button } from "@/components/ui/button";
import { Card } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { useDisplayFormat } from "./displayPreferences";
type Usage={name:string;pool:string;type:string;usedBytes:number|null;availableBytes:number|null;snapshotUsedBytes:number|null;quotaBytes:number|null;refQuotaBytes:number|null};
type Sample={id:number;configurationRevision:string;fetchedUtc:string;systemSerial:string|null;version:string|null;snapshot:{fetchedUtc:string;sourceSchema:string;items:Usage[]}};
type Page={snapshotId:number;items:Sample[];nextBefore:number|null;retentionDays:number;sampling:string};
function valid(value:unknown):value is Page {
 const object=(x:unknown):x is Record<string,unknown>=>!!x&&typeof x==='object'&&!Array.isArray(x);
 const date=(x:unknown)=>typeof x==='string'&&Number.isFinite(Date.parse(x));
 if(!object(value)||!Number.isSafeInteger(value.snapshotId)||Number(value.snapshotId)<0||!Array.isArray(value.items)||value.items.length>10||!Number.isInteger(value.retentionDays)||typeof value.sampling!=='string'||!(value.nextBefore===null||Number.isSafeInteger(value.nextBefore)&&Number(value.nextBefore)>0))return false;
 const ids=new Set<number>();
 return value.items.every(row=>{
  if(!object(row)||!Number.isSafeInteger(row.id)||Number(row.id)<=0||ids.has(Number(row.id))||!date(row.fetchedUtc)||typeof row.configurationRevision!=='string'||![row.systemSerial,row.version].every(x=>x===null||typeof x==='string')||!object(row.snapshot)||!date(row.snapshot.fetchedUtc)||typeof row.snapshot.sourceSchema!=='string'||!Array.isArray(row.snapshot.items)||row.snapshot.items.length>256)return false;
  ids.add(Number(row.id));const names=new Set<string>();
  return row.snapshot.items.every(item=>{if(!object(item)||typeof item.name!=='string'||!item.name||names.has(item.name)||typeof item.pool!=='string'||!['FILESYSTEM','VOLUME'].includes(String(item.type)))return false;names.add(item.name);return ['usedBytes','availableBytes','snapshotUsedBytes','quotaBytes','refQuotaBytes'].every(key=>item[key]===null||Number.isSafeInteger(item[key])&&Number(item[key])>=0);});
 });
}
export function DatasetHistoryView({connectionId}:{connectionId:string}) {
 const [open,setOpen]=useState(false);const id=useId();
 return <div style={{marginTop:20}}><Button variant="outline" aria-expanded={open} aria-controls={id} onClick={()=>setOpen(x=>!x)}>{open?'Hide sampled dataset history':'Browse sampled dataset history'}</Button>
 {open&&<section id={id} aria-label="Sampled dataset inventory history"><History key={connectionId} connectionId={connectionId}/></section>}</div>;
}
async function read(connectionId:string,previous:Page|null,signal:AbortSignal){const query=new URLSearchParams();if(previous?.nextBefore){query.set('snapshotId',String(previous.snapshotId));query.set('before',String(previous.nextBefore));}const response=await fetch(`/api/v1/connections/${connectionId}/dataset-history?${query}`,{signal:AbortSignal.any([signal,AbortSignal.timeout(15000)])});if(!response.ok)throw new Error(`Dataset history could not be loaded (${response.status}).`);const result:unknown=await response.json();if(!valid(result))throw new Error('Dataset history response was incomplete.');return result;}
function History({connectionId}:{connectionId:string}) {
 const {time,bytes}=useDisplayFormat();const id=useId();const [page,setPage]=useState<Page|null>(null);const [selected,setSelected]=useState(0);const [search,setSearch]=useState('');const [limit,setLimit]=useState(20);const [busy,setBusy]=useState(true);const [error,setError]=useState('');const lifetime=useRef<AbortController|null>(null);

 useEffect(()=>{const controller=new AbortController();lifetime.current=controller;void read(connectionId,null,controller.signal).then(result=>{if(!controller.signal.aborted)setPage(result);}).catch(error=>{if(!controller.signal.aborted)setError(error instanceof Error?error.message:'History unavailable.');}).finally(()=>{if(!controller.signal.aborted)setBusy(false);});return ()=>controller.abort();},[connectionId]);
 async function load(previous:Page|null){if(busy||!lifetime.current)return;const signal=lifetime.current.signal;setBusy(true);setError('');try{const result=await read(connectionId,previous,signal);if(!signal.aborted){setPage(result);setSelected(0);setLimit(20);}}catch(error){if(!signal.aborted)setError(error instanceof Error?error.message:'History unavailable.');}finally{if(!signal.aborted)setBusy(false);}}
 const sample=page?.items[selected];const rows=sample?.snapshot.items??[];const matches=rows.filter(x=>x.name.toLocaleLowerCase().includes(search.trim().toLocaleLowerCase()));const quota=(x:number|null)=>x===0?'No quota set':bytes(x);
 return <div className="dataset-usage"><h3>Sampled inventory history</h3><p className="muted">Historical inventory, not current status. The first successful read per UTC hour and connection revision is retained. Gaps do not prove deletion, and matching names do not prove the same dataset.</p>
 {error&&<p role="alert" className="error">{error} {page?'Previously loaded history remains visible.':''}</p>}
 <p role="status">{busy?'Loading sampled inventory…':page?`${page.items.length} saved inventories on this page. ${page.nextBefore?'Older samples are available.':'End of retained samples.'}`:'History unavailable.'}</p>
 <div className="review-actions">{page?.nextBefore&&<Button variant="outline" disabled={busy} onClick={()=>void load(page)}>Older dataset samples</Button>}<Button variant="outline" disabled={busy} onClick={()=>void load(null)}>{page?'Refresh latest samples':'Retry dataset history'}</Button></div>
 {page&&<p className="muted">Retention target: {page.retentionDays} days. Expiry requires a successful backup. Inventories referenced by rename reviews remain retained. Refresh includes new samples; no older history is reconstructed.</p>}
 {page&&!page.items.length&&<p>No sampled inventories have been retained yet. Samples begin with successful collection after history support is enabled.</p>}
 {sample&&<><div className="field"><Label htmlFor={`${id}-sample`}>Saved inventory</Label><SelectField id={`${id}-sample`} value={selected} onValueChange={event=>{setSelected(Number(event));setLimit(20);}}>{page!.items.map((row,index)=><SelectOption value={index} key={row.id}>{time(row.fetchedUtc)} · record {row.id}</SelectOption>)}</SelectField></div>
 <details><summary>Recorded source context</summary><p>System identity: {sample.systemSerial||'Unknown'} · Version: {sample.version||'Unknown'}</p><p className="muted" style={{overflowWrap:'anywhere'}}>Connection revision: {sample.configurationRevision} · Source: {sample.snapshot.sourceSchema}. A changed source context must be reviewed before comparing inventories.</p></details>
 {!rows.length?<p>This successful sample contained no visible datasets. It does not establish that the NAS contained no data.</p>:<><div className="field"><Label htmlFor={`${id}-search`}>Find a name in this saved inventory</Label><Input id={`${id}-search`} type="search" value={search} onChange={event=>{setSearch(event.target.value);setLimit(20);}}/></div><p className="muted">Showing {Math.min(limit,matches.length)} of {matches.length} matching entries · {rows.length} saved. Parent and descendant usage overlaps.</p>
 {!matches.length&&<p>No names match. <Button variant="ghost" onClick={()=>setSearch('')}>Clear history filter</Button></p>}
 <div className="dataset-list">{matches.slice(0,limit).map(row=><Card className="dataset-card" key={row.name}><h4 style={{overflowWrap:'anywhere'}}>{row.name}</h4><p className="muted">{row.type==='VOLUME'?'Volume':'Filesystem'} · Historical sample</p><dl className="dataset-metrics"><div><dt>Used, including descendants</dt><dd>{bytes(row.usedBytes)}</dd></div><div><dt>Available to this dataset</dt><dd>{bytes(row.availableBytes)}</dd></div><div><dt>Used by its snapshots</dt><dd>{bytes(row.snapshotUsedBytes)}</dd></div></dl><details><summary>Historical quota details</summary><dl className="dataset-metrics"><div><dt>Dataset quota</dt><dd>{quota(row.quotaBytes)}</dd></div><div><dt>Referenced-data quota</dt><dd>{quota(row.refQuotaBytes)}</dd></div></dl></details></Card>)}</div>{matches.length>limit&&<Button variant="outline" onClick={()=>setLimit(x=>x+20)}>Show more historical datasets</Button>}</>}
 </>}
 <DatasetRenameView connectionId={connectionId} current={sample?{id:sample.id,fetchedUtc:sample.fetchedUtc}:null}/>
 </div>;
}
