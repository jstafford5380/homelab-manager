import { SelectField, SelectOption } from "@/components/SelectField";
import { useEffect, useRef, useState } from "react";
import { Card } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Dialog, DialogContent, DialogHeader, DialogTitle, DialogDescription } from "@/components/ui/dialog";

type Acquisition = {
  diskId: string; revision: string; installedUtc: string | null; installationConfidence: string;
  installationEvidence: string; purchaseCost: number | null; currency: string; costConfidence: string;
  purchaseEvidence: string; author: string; recordedUtc: string;
};
function valid(value: Acquisition, diskId: string) {
  return value?.diskId === diskId && typeof value.revision === "string" &&
    [value.installationConfidence, value.installationEvidence, value.currency, value.costConfidence, value.purchaseEvidence, value.author].every(x => typeof x === "string") &&
    (value.purchaseCost === null || Number.isFinite(value.purchaseCost)) &&
    (value.installedUtc === null || Number.isFinite(Date.parse(value.installedUtc))) && Number.isFinite(Date.parse(value.recordedUtc));
}
export default function DriveAcquisitionView({ diskId, timezone }: { diskId: string; timezone: string }) {
  const [record, setRecord] = useState<Acquisition | null>();
  const [reload, setReload] = useState(0);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [message, setMessage] = useState("");
  const [open, setOpen] = useState(false);
  const [busy, setBusy] = useState(false);
  const alertRef = useRef<HTMLParagraphElement>(null);
  useEffect(() => { if (error && open) alertRef.current?.focus(); }, [error, open]);
  useEffect(() => {
    const controller = new AbortController();
    void (async () => {
      try {
        const response = await fetch(`/api/v1/disks/${diskId}/acquisition`, { signal: AbortSignal.any([controller.signal, AbortSignal.timeout(15000)]) });
        if (!response.ok) throw new Error("Acquisition notes could not be loaded.");
        const body = await response.json();
        if (!(body.record === null || valid(body.record, diskId))) throw new Error("Acquisition notes response was not recognized.");
        if (!controller.signal.aborted) { setRecord(body.record); setError(""); }
      } catch (e) { if (!controller.signal.aborted) setError(e instanceof Error ? e.message : "Acquisition notes could not be loaded."); }
      finally { if (!controller.signal.aborted) setLoading(false); }
    })();
    return () => controller.abort();
  }, [diskId, reload]);
  const reloadSaved = () => { setOpen(false); setLoading(true); setMessage(""); setReload(x => x + 1); };
  return <Card className="list-card"><section aria-label="Drive acquisition notes">
    <h2>Installation & purchase evidence</h2>
    <p className="muted">Manual history for this physical drive. First discovery and power-on hours do not establish installation date or remaining life. Purchase cost is separate from replacement budgets.</p>
    {loading && <p role="status">Loading saved notes…</p>}
    {record !== undefined && <>
      <p>Installed: {record?.installedUtc ? `${record.installedUtc.slice(0,10)} (${record.installationConfidence})` : "Unknown"}</p>
      <p>Purchase cost: {record?.purchaseCost != null ? `${record.purchaseCost} ${record.currency} (${record.costConfidence})` : "Unknown"}</p>
      {record && <details><summary>Acquisition evidence and author</summary>
        <p className="whitespace-pre-wrap">Installation evidence: {record.installationEvidence || "Not recorded"}</p>
        <p className="whitespace-pre-wrap">Purchase evidence: {record.purchaseEvidence || "Not recorded"}</p>
        <small>{record.author} · Recorded {new Date(record.recordedUtc).toLocaleString(undefined, { timeZone: timezone })}</small>
      </details>}
    </>}
    {!open && error && <p role="alert" className="error">{error} {record !== undefined && "Previously loaded notes remain visible."}</p>}
    {!open && message && <p role="status">{message}</p>}
    <div className="review-actions">
      <Button variant="outline" disabled={loading || record === undefined} onClick={() => { setError(""); setMessage(""); setOpen(true); }}>Edit purchase & installation</Button>
      <Button variant="ghost" disabled={loading} onClick={reloadSaved}>Refresh acquisition notes</Button>
    </div>
    <Dialog open={open} onOpenChange={value => { if (!busy) setOpen(value); }}><DialogContent style={{maxHeight:"85vh",overflow:"auto"}}>
      <DialogHeader><DialogTitle>Purchase & installation notes</DialogTitle><DialogDescription>Record what you know, including uncertainty. These notes only change local records and never change the NAS.</DialogDescription></DialogHeader>
      <form key={record?.revision || "new"} onSubmit={async event => {
        event.preventDefault(); const fields = new FormData(event.currentTarget);
        setBusy(true); setError("");
        try {
          const response = await fetch(`/api/v1/disks/${diskId}/acquisition`, { method: "PUT", signal: AbortSignal.timeout(15000),
            headers: { "Content-Type":"application/json", "X-Homelab-Request":"1" },
            body: JSON.stringify({ revision: record?.revision ?? null,
              installedUtc: fields.get("installedUtc") ? `${fields.get("installedUtc")}T00:00:00Z` : null,
              installationConfidence: fields.get("installationConfidence"), installationEvidence: fields.get("installationEvidence"),
              purchaseCost: fields.get("purchaseCost") === "" ? null : Number(fields.get("purchaseCost")), currency: fields.get("currency"),
              costConfidence: fields.get("costConfidence"), purchaseEvidence: fields.get("purchaseEvidence") }) });
          if (!response.ok) { const body = await response.json(); throw new Error(body.error || "Notes could not be saved. Your draft is still here."); }
          const body = await response.json(); if (!valid(body, diskId)) throw new Error("Save response was not recognized. Reload saved notes before retrying.");
          setRecord(body); setOpen(false); setMessage("Acquisition notes saved. Earlier values remain in the local audit history.");
        } catch (e) { setError(e instanceof Error ? e.message : "Notes could not be saved. Your draft is still here."); }
        finally { setBusy(false); }
      }}>
        <div className="field"><Label htmlFor="acquisition-installation">Installation date (optional)</Label><Input id="acquisition-installation" name="installedUtc" type="date" defaultValue={record?.installedUtc?.slice(0,10) || ""} /></div>
        <div className="field"><Label htmlFor="acquisition-installation-confidence">Installation-date confidence</Label><SelectField id="acquisition-installation-confidence" name="installationConfidence" defaultValue={record?.installationConfidence || "unknown"}>{["unknown","estimated","documented"].map(x => <SelectOption key={x}>{x}</SelectOption>)}</SelectField></div>
        <div className="field"><Label htmlFor="acquisition-installation-evidence">Installation evidence or estimate basis</Label><textarea id="acquisition-installation-evidence" name="installationEvidence" rows={2} maxLength={4000} defaultValue={record?.installationEvidence || ""} /></div>
        <div className="settings-grid">
          <div className="field"><Label htmlFor="acquisition-cost">Original purchase cost (optional)</Label><Input id="acquisition-cost" name="purchaseCost" type="number" min={0} max={1000000000} step="any" defaultValue={record?.purchaseCost ?? ""} /></div>
          <div className="field"><Label htmlFor="acquisition-currency">Purchase currency (three letters)</Label><Input id="acquisition-currency" name="currency" maxLength={3} defaultValue={record?.currency || ""} /></div>
        </div>
        <div className="field"><Label htmlFor="acquisition-cost-confidence">Purchase-cost confidence</Label><SelectField id="acquisition-cost-confidence" name="costConfidence" defaultValue={record?.costConfidence || "unknown"}>{["unknown","estimated","documented"].map(x => <SelectOption key={x}>{x}</SelectOption>)}</SelectField></div>
        <div className="field"><Label htmlFor="acquisition-purchase-evidence">Receipt reference or purchase evidence</Label><textarea id="acquisition-purchase-evidence" name="purchaseEvidence" rows={2} maxLength={4000} defaultValue={record?.purchaseEvidence || ""} /></div>
        <p className="muted">Leave unknown dates and costs blank with unknown confidence. A zero cost means the drive was free. Record references, never payment credentials. Purchase date and warranty remain in Drive notes.</p>
        {error && <p role="alert" tabIndex={-1} ref={alertRef} className="error">{error}</p>}
        <div className="review-actions"><Button disabled={busy}>{busy ? "Saving…" : "Save acquisition notes"}</Button><Button type="button" variant="outline" disabled={busy} onClick={reloadSaved}>Reload saved notes & discard draft</Button></div>
      </form>
    </DialogContent></Dialog>
  </section></Card>;
}
