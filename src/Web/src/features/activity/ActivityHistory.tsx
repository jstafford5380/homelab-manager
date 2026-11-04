import {useEffect,useRef,useState} from "react";
import {Button} from "@/components/ui/button";
import {Card} from "@/components/ui/card";
import {useDisplayFormat} from "../../lib/displayPreferences";
type Page = {snapshotId:number;items:{id:number;atUtc:string;actor:string;action:string;detail:string}[];nextBefore:number|null};
export function ActivityHistory() {
 const {time}=useDisplayFormat();const [page,setPage]=useState<Page|null>(null);const [busy,setBusy]=useState(true);const [error,setError]=useState('');const lifetime=useRef<AbortController|null>(null);
 async function read(previous:Page|null,signal:AbortSignal){
  const query=new URLSearchParams();if(previous?.nextBefore){query.set('snapshotId',String(previous.snapshotId));query.set('before',String(previous.nextBefore));}
  const response=await fetch(`/api/v1/activity/history?${query}`,{signal:AbortSignal.any([signal,AbortSignal.timeout(15000)])});
  if(!response.ok)throw new Error(`Activity could not be loaded (${response.status}).`);
  const result=await response.json() as Page;if(!Array.isArray(result.items))throw new Error('Activity response was incomplete.');
  return result;
 }
 useEffect(()=>{const controller=new AbortController();lifetime.current=controller;
  void read(null,controller.signal).then(result=>{if(!controller.signal.aborted)setPage(result);}).catch(error=>{if(!controller.signal.aborted)setError(error instanceof Error?error.message:'Activity unavailable.');}).finally(()=>{if(!controller.signal.aborted)setBusy(false);});return ()=>controller.abort();
 },[]);
 async function load(previous:Page|null){if(busy||!lifetime.current)return;const signal=lifetime.current.signal;setBusy(true);setError('');try{const result=await read(previous,signal);if(!signal.aborted)setPage(result);}catch(error){if(!signal.aborted)setError(error instanceof Error?error.message:'Activity unavailable.');}finally{if(!signal.aborted)setBusy(false);}}
 return <Card className="list-card" aria-label="Retained activity history">
  <div style={{padding:24}}><h2>Activity history</h2><p className="muted">Collection events and operator changes, newest recorded first. Each page shows up to 50 retained events. Refresh to include new activity.</p>
  {error&&<p role="alert">{error} {page?'Previously loaded events remain visible.':''}</p>}
  <p role="status">{busy?'Loading activity…':page?`${page.items.length} events on this page. ${page.nextBefore?'Older events are available.':'End of retained history.'}`:'Activity unavailable.'}</p>
  <div className="review-actions">{page?.nextBefore&&<Button variant="outline" disabled={busy} onClick={()=>void load(page)}>Older activity</Button>}<Button variant="outline" disabled={busy} onClick={()=>void load(null)}>{page?'Refresh latest activity':'Retry activity'}</Button></div></div>
  {page?.items.map(event=><article className="row" key={event.id}><div style={{minWidth:0,overflowWrap:'anywhere'}}><h3>{event.action}</h3><p style={{whiteSpace:'pre-wrap'}}>{event.detail}</p><small>{event.actor} · <time dateTime={event.atUtc}>{time(event.atUtc)}</time></small></div></article>)}
  {page&&!page.items.length&&<div className="empty"><h3>No retained activity yet</h3><p>Collection events and operator changes will appear here.</p></div>}
 </Card>;
}
