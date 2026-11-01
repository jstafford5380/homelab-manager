import { useEffect, useRef, useState } from "react";
import { Link } from "react-router";
import { Button } from "@/components/ui/button";
import { Card } from "@/components/ui/card";
import { Dialog, DialogContent, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";
import { useDisplayFormat } from "./displayPreferences";
type Asset = {id:string; name:string; type:string; updatedUtc:string; archived:boolean};
type RecordRow = {id:number; assetName:string; assetType:string; attached:boolean; evidence:string; recordedUtc:string; validUntilUtc:string; author:string};
type Head = {assetId:string;id:number;attached:boolean;evidence:string;recordedUtc:string};
type Page = {generatedUtc:string; currentHeads?:Head[]; items:{record:RecordRow;status:string}[]; nextBefore:number|null; peersTruncated:boolean; peers:{diskId:string;serial:string;model:string;components:{assetId:string;assetName:string;assetType:string;validUntilUtc:string}[];findings:{id:string;title:string;evidence:string;evidenceObservedUtc:string|null;lastSeenUtc:string;state:string}[]}[]};
async function request(url:string,body?:unknown) {
 const controller=new AbortController();const timer=setTimeout(()=>controller.abort(),15000);
 try {const response=await fetch(url,{signal:controller.signal,...(body?{method:"POST",headers:{"Content-Type":"application/json","X-Homelab-Request":"1"},body:JSON.stringify(body)}:{})});const data=await response.json();if(!response.ok)throw Error(data.error||"Could not load or save hardware evidence.");return data;} finally {clearTimeout(timer);}
}
export function DriveHardwareView({disk}:{disk:{id:string;serial:string;wwn?:string|null}}) {
 const {time}=useDisplayFormat();const [expanded,setExpanded]=useState(false);const [page,setPage]=useState<Page|null>(null);const [before,setBefore]=useState<number|null>(null);const [revision,setRevision]=useState(0);const [error,setError]=useState("");const [busy,setBusy]=useState(false);
 const [reviewHeads,setReviewHeads]=useState<Head[]>([]);const [selectedAsset,setSelectedAsset]=useState("");
 const [dialog,setDialog]=useState(false);const [assets,setAssets]=useState<Asset[]>([]);const [message,setMessage]=useState("");const messageRef=useRef<HTMLParagraphElement>(null);const attempt=useRef<{key:string;id:string}|null>(null);
 useEffect(()=>{if(message)messageRef.current?.focus();},[message]);
 useEffect(()=>{if(!expanded)return;let active=true;const load=async()=>{try{const data:Page=await request(`/api/v1/disks/${disk.id}/hardware-paths${before?`?before=${before}`:""}`);if(!Array.isArray(data.items)||!Array.isArray(data.peers))throw Error("Hardware evidence could not be read safely.");if(active){setPage(data);setError("");}}catch(e){if(active)setError(e instanceof Error?e.message:"Hardware evidence unavailable.");}};void load();const timer=setInterval(()=>void load(),30000);return()=>{active=false;clearInterval(timer);};},[expanded,disk.id,before,revision]);
 async function open(){setBusy(true);setMessage("");try{const [data,latest]=await Promise.all([request("/api/v1/inventory"),request(`/api/v1/disks/${disk.id}/hardware-paths`)]);setPage(latest);setReviewHeads(latest.currentHeads??[]);if(!dialog)setSelectedAsset("");setBefore(null);setAssets(data.assets.filter((a:Asset)=>["controller","enclosure","power"].includes(a.type)));setDialog(true);attempt.current=null;}catch(e){setMessage(e instanceof Error?e.message:"Inventory unavailable.");}finally{setBusy(false);}}
 return <section className="drive-hardware" aria-label="Shared hardware evidence">
  <Button variant="outline" aria-expanded={expanded} onClick={()=>setExpanded(v=>!v)}>{expanded?"Hide shared hardware":"Shared hardware & related findings"}</Button>
  {message&&!dialog&&<p tabIndex={-1} ref={messageRef} role="status">{message}</p>}
  {expanded&&<>
   <p className="muted">Record known controller, enclosure and power relationships. Related findings help guide investigation; they do not prove a shared cause or verify a physical bay.</p>
   <Button variant="outline" disabled={busy} onClick={()=>void open()}>Record hardware relationship</Button>
   {error&&<p role="alert">{error} {page?"Showing last loaded history; related findings are hidden.":""}<Button variant="outline" onClick={()=>setRevision(v=>v+1)}>Retry hardware evidence</Button></p>}
   {!page&&!error&&<p>Loading hardware evidence…</p>}
   {page&&<><p className="muted">Assessed {time(page.generatedUtc)}. Relationships expire or need review when inventory or drive identity changes.</p>
    {!page.items.length&&<p>No hardware relationship records on this page. Add the component in <Link to="/inventory">Inventory</Link>, then record its relationship here.</p>}
    {page.items.map(({record:r,status})=><Card key={r.id} className="daily-summary"><h4>{r.assetName} · {r.assetType}</h4><p>{status} · {r.attached?"Attached relationship recorded":"Relationship ended"}</p><p>{r.evidence}</p><small>Recorded {time(r.recordedUtc)} by {r.author}. Review expiry: {time(r.validUntilUtc)}.</small></Card>)}
    <div className="review-actions">{before&&<Button variant="outline" onClick={()=>{setPage(null);setBefore(null);}}>Newest relationships</Button>}{page.nextBefore&&<Button variant="outline" onClick={()=>{setPage(null);setBefore(page.nextBefore);}}>Older relationships</Button>}</div>
    {!error&&<><h4>Other drives on the same recorded components</h4><p className="muted">Open findings may describe older evidence and different times. No finding listed is not proof of health. Physical relationships are operator-recorded, not automatically discovered.</p>
     {!page.peers.length&&<p>No other drives have a currently valid recorded relationship to these components.</p>}
     {page.peers.map(peer=><Card key={peer.diskId} className="daily-summary"><h4>{peer.model} · {peer.serial||"Serial unknown"}</h4><p>{peer.components.map(c=>c.assetName).join(", ")}</p><Link to={`/storage?disk=${encodeURIComponent(peer.diskId)}`}>View related drive evidence</Link>{!peer.findings.length?<p>No open finding in the returned evidence.</p>:peer.findings.map(f=><div key={f.id}><p><Link to={`/?finding=${encodeURIComponent(f.id)}`}>{f.title}</Link> · {f.state}</p><p>{f.evidence}</p><small>Evidence time: {f.evidenceObservedUtc?time(f.evidenceObservedUtc):"Unknown"} · Last recorded: {time(f.lastSeenUtc)}</small></div>)}</Card>)}
     {page.peersTruncated&&<p>Results are limited to 32 related drives and 160 findings. Review the full findings list for additional evidence.</p>}
    </>}
   </>}
  </>}
  <Dialog open={dialog} onOpenChange={value=>{if(!busy)setDialog(value);}}><DialogContent className="replacement-dialog"><DialogHeader><DialogTitle>Record hardware relationship</DialogTitle></DialogHeader>
   <p>Save local evidence or end an earlier relationship. Earlier records remain in history. This does not change the NAS.</p>
   <form onSubmit={async e=>{e.preventDefault();const fields=new FormData(e.currentTarget);const asset=assets.find(a=>a.id===fields.get("asset"));if(!asset)return;setBusy(true);setMessage("");try{const payload={previousReviewId:reviewHeads.find(x=>x.assetId===asset.id)?.id??null,assetId:asset.id,assetUpdatedUtc:asset.updatedUtc,confirmedIdentity:fields.get("identity"),attached:fields.get("attached")==="yes",evidence:fields.get("evidence"),validUntilUtc:new Date(String(fields.get("expiry"))+"T23:59:59Z").toISOString()};const key=JSON.stringify(payload);if(attempt.current?.key!==key)attempt.current={key,id:crypto.randomUUID()};await request(`/api/v1/disks/${disk.id}/hardware-paths`,{...payload,requestId:attempt.current.id});setDialog(false);setBefore(null);setRevision(v=>v+1);setMessage("Hardware evidence recorded locally. NAS unchanged.");}catch(e){setMessage(e instanceof Error?e.message:"Could not save hardware evidence.");}finally{setBusy(false);}}}>
    <div className="field"><Label htmlFor="hardware-asset">Component</Label><select id="hardware-asset" name="asset" required value={selectedAsset} onChange={e=>setSelectedAsset(e.target.value)}><option value="" disabled>Choose an inventory component</option>{assets.map(a=><option key={a.id} value={a.id}>{a.name} · {a.type}{a.archived?" (archived; end relationship only)":""}</option>)}</select></div>
    {reviewHeads.filter(x=>x.assetId===selectedAsset).map(head=><div key={head.id}><strong>Latest relationship reviewed in this form</strong><p>{head.attached?"Attached":"Ended"} · {time(head.recordedUtc)}</p><p>{head.evidence}</p></div>)}
    {!assets.length&&<p>Add a controller, enclosure or power asset in Inventory first.</p>}
    <div className="field"><Label htmlFor="hardware-attached">Relationship</Label><select id="hardware-attached" name="attached"><option value="yes">Attached to this component</option><option value="no">No longer attached</option></select></div>
    <div className="field"><Label htmlFor="hardware-identity">Confirm drive serial or WWN</Label><Input id="hardware-identity" name="identity" required autoComplete="off"/><small>Match: {disk.serial?.trim()?disk.serial:disk.wwn?.trim().toLowerCase().replaceAll("0x","").replaceAll(":","").replaceAll("-","")||"Unknown identity"}</small></div>
    <div className="field"><Label htmlFor="hardware-evidence">Relationship evidence</Label><Textarea id="hardware-evidence" name="evidence" required maxLength={4000}/></div>
    <div className="field"><Label htmlFor="hardware-expiry">Review expiry (UTC)</Label><Input id="hardware-expiry" name="expiry" type="date" required/></div>
    {message&&<p ref={messageRef} tabIndex={-1} role="alert">{message}</p>}
    <div className="review-actions"><Button type="button" variant="outline" disabled={busy} onClick={()=>setDialog(false)}>Cancel</Button><Button type="button" variant="outline" disabled={busy} onClick={()=>void open()}>Reload latest evidence</Button><Button disabled={busy||!assets.length}>{busy?"Saving…":"Save relationship evidence"}</Button></div>
   </form>
  </DialogContent></Dialog>
 </section>;
}
