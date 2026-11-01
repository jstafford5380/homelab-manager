import { useEffect, useState } from "react";
import { Card } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Label } from "@/components/ui/label";
import { Input } from "@/components/ui/input";
import { Badge } from "@/components/ui/badge";
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogDescription,
} from "@/components/ui/dialog";

type Drive = {
  id: string;
  connectionId: string;
  model: string;
  serial: string;
  wwn: string | null;
  mappingConfidence: string;
};
type Replacement = {
  id: string;
  oldDiskId: string;
  newDiskId: string;
  oldIdentity: string;
  newIdentity: string;
  occurredUtc: string;
  dateConfidence: string;
  poolGuid: string;
  membershipEvidence: string;
  reason: string;
  evidence: string;
  author: string;
  recordedUtc: string;
  voidedUtc: string | null;
  voidReason: string | null;
  voidedBy: string | null;
};
const identity = (disk: Drive) =>
  disk.serial.trim() ? disk.serial : disk.wwn || "";
export function ReplacementHistory({
  disk,
  disks,
  timezone,
  onSelect,
}: {
  disk: Drive;
  disks: Drive[];
  timezone: string;
  onSelect: (id: string) => void;
}) {
  const [records, setRecords] = useState<Replacement[] | null>(null);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");
  const [open, setOpen] = useState(false);
  const [correction, setCorrection] = useState<Replacement | null>(null);
  const [newId, setNewId] = useState("");
  const [busy, setBusy] = useState(false);
  const [revision, setRevision] = useState(0);
  useEffect(() => {
    const controller = new AbortController();
    async function load() {
      try {
        const response = await fetch(`/api/v1/disks/${disk.id}/replacements`, {
          signal: controller.signal,
        });
        if (!response.ok)
          throw new Error("Could not load replacement records.");
        const data = await response.json();
        if (!controller.signal.aborted) {
          setRecords(data);
          setError("");
        }
      } catch (e) {
        if (!controller.signal.aborted) setError(String(e));
      }
    }
    void load();
    return () => controller.abort();
  }, [disk.id, revision]);
  const candidates = disks.filter(
    (d) =>
      d.id !== disk.id &&
      d.connectionId === disk.connectionId &&
      d.mappingConfidence !== "ambiguous" &&
      identity(d),
  );
  const next = candidates.find((d) => d.id === newId);
  const timestamp = (value: string) =>
    new Date(value).toLocaleString(undefined, { timeZone: timezone });
  async function submit(path: string, body: unknown) {
    setBusy(true);
    setError("");
    try {
      const response = await fetch(`/api/v1${path}`, {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
          "X-Homelab-Request": "1",
        },
        body: JSON.stringify(body),
      });
      if (!response.ok) {
        const data = await response.json();
        throw new Error(data.error || "Could not save the record.");
      }
      setOpen(false);
      setCorrection(null);
      setRevision((x) => x + 1);
      setNotice(
        "Local history saved. Drive histories and NAS state are unchanged.",
      );
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setBusy(false);
    }
  }
  return (
    <section className="replacement-history">
      <div className="section-title">
        <h2>Replacement history</h2>
        <Button
          variant="outline"
          disabled={
            !candidates.length ||
            disk.mappingConfidence === "ambiguous" ||
            !identity(disk)
          }
          onClick={() => {
            setOpen(true);
            setError("");
            setNewId("");
          }}
        >
          Record a past replacement
        </Button>
      </div>
      <p className="muted">
        Link an already completed replacement to this drive’s history. Each
        physical drive keeps its own observations and findings.
      </p>
      {!candidates.length && (
        <p className="muted">
          A second identified drive from this NAS is needed to record a
          replacement. Collect inventory after the new drive appears.
        </p>
      )}
      {disk.mappingConfidence === "ambiguous" && (
        <p className="muted">
          Resolve this drive’s identity before linking a replacement.
        </p>
      )}
      {error && !open && !correction && (
        <div role="alert">
          <p className="error">{error}</p>
          <Button variant="outline" onClick={() => setRevision((x) => x + 1)}>
            Retry history
          </Button>
        </div>
      )}
      {notice && <p role="status">{notice}</p>}
      <Card className="list-card">
        {records === null ? (
          <p className="planning-empty">Loading replacement records…</p>
        ) : records.length === 0 ? (
          <p className="planning-empty">
            No replacements recorded for this drive.
          </p>
        ) : (
          records.map((record) => (
            <article className="replacement-record" key={record.id}>
              <div className="section-title">
                <h3>
                  {record.oldIdentity} → {record.newIdentity}
                </h3>
                <Badge variant="outline">
                  {record.voidedUtc ? "Marked incorrect" : "Operator recorded"}
                </Badge>
              </div>
              <p>
                {timestamp(record.occurredUtc)} · {record.dateConfidence} date
              </p>
              <p>{record.reason}</p>
              <details>
                <summary>Evidence & membership</summary>
                <p>Pool GUID: {record.poolGuid || "Unknown"}</p>
                <p>{record.membershipEvidence}</p>
                <p>{record.evidence}</p>
                <small>
                  Recorded by {record.author} at {timestamp(record.recordedUtc)}
                </small>
              </details>
              {record.voidedUtc && (
                <p className="muted">
                  Marked incorrect by {record.voidedBy} at{" "}
                  {timestamp(record.voidedUtc)}: {record.voidReason}. Original
                  evidence is retained.
                </p>
              )}
              <div className="review-actions">
                <Button
                  variant="outline"
                  onClick={() =>
                    onSelect(
                      record.oldDiskId === disk.id
                        ? record.newDiskId
                        : record.oldDiskId,
                    )
                  }
                >
                  View {record.oldDiskId === disk.id ? "new" : "previous"} drive
                </Button>
                {!record.voidedUtc && (
                  <Button
                    variant="ghost"
                    onClick={() => {
                      setCorrection(record);
                      setError("");
                    }}
                  >
                    Mark record incorrect
                  </Button>
                )}
              </div>
            </article>
          ))
        )}
      </Card>
      <Dialog
        open={open}
        onOpenChange={(value) => {
          if (!busy) setOpen(value);
        }}
      >
        <DialogContent className="replacement-dialog">
          <DialogHeader>
            <DialogTitle>Record a past replacement</DialogTitle>
            <DialogDescription>
              This records history only. It does not replace a disk, move
              observations, retire a drive, or clear findings.
            </DialogDescription>
          </DialogHeader>
          <form
            onSubmit={(event) => {
              event.preventDefault();
              const fields = new FormData(event.currentTarget);
              void submit("/replacements", {
                oldDiskId: disk.id,
                newDiskId: newId,
                occurredUtc: new Date(
                  String(fields.get("occurred")),
                ).toISOString(),
                dateConfidence: fields.get("confidence"),
                poolGuid: fields.get("pool"),
                membershipEvidence: fields.get("membership"),
                reason: fields.get("reason"),
                evidence: fields.get("evidence"),
                confirmedOldIdentity: fields.get("oldIdentity"),
                confirmedNewIdentity: fields.get("newIdentity"),
              });
            }}
          >
            {error && (
              <p className="error" role="alert">
                {error}
              </p>
            )}
            <p>
              <strong>Previous drive:</strong> {disk.model} · {identity(disk)}
            </p>
            <div className="field">
              <Label htmlFor="replacement-new">New physical drive</Label>
              <select
                id="replacement-new"
                required
                value={newId}
                onChange={(e) => setNewId(e.target.value)}
              >
                <option value="">Choose an identified drive</option>
                {candidates.map((d) => (
                  <option value={d.id} key={d.id}>
                    {d.model} · {identity(d)}
                  </option>
                ))}
              </select>
            </div>
            <div className="settings-grid">
              <div className="field">
                <Label htmlFor="replacement-old-confirm">
                  Retype previous serial / WWN
                </Label>
                <Input
                  id="replacement-old-confirm"
                  name="oldIdentity"
                  required
                  autoComplete="off"
                />
              </div>
              <div className="field">
                <Label htmlFor="replacement-new-confirm">
                  Retype new serial / WWN
                </Label>
                <Input
                  id="replacement-new-confirm"
                  name="newIdentity"
                  required
                  autoComplete="off"
                  placeholder={next ? identity(next) : "Choose a drive above"}
                />
              </div>
            </div>
            <div className="settings-grid">
              <div className="field">
                <Label htmlFor="replacement-time">
                  Replacement time (browser timezone)
                </Label>
                <Input
                  id="replacement-time"
                  name="occurred"
                  type="datetime-local"
                  required
                />
              </div>
              <div className="field">
                <Label htmlFor="replacement-confidence">Date confidence</Label>
                <select
                  id="replacement-confidence"
                  name="confidence"
                  defaultValue="documented"
                >
                  <option value="documented">Documented</option>
                  <option value="estimated">Estimated</option>
                </select>
              </div>
            </div>
            <div className="field">
              <Label htmlFor="replacement-pool">Pool GUID (if known)</Label>
              <Input id="replacement-pool" name="pool" maxLength={100} />
            </div>
            <div className="field">
              <Label htmlFor="replacement-membership">
                Pool / vdev membership evidence
              </Label>
              <Input
                id="replacement-membership"
                name="membership"
                required
                maxLength={4000}
                placeholder="Identify the pool and member, or explain what is unknown"
              />
            </div>
            <div className="field">
              <Label htmlFor="replacement-reason">Reason for replacement</Label>
              <Input
                id="replacement-reason"
                name="reason"
                required
                maxLength={2000}
              />
            </div>
            <div className="field">
              <Label htmlFor="replacement-evidence">
                Operator evidence / reference
              </Label>
              <Input
                id="replacement-evidence"
                name="evidence"
                required
                maxLength={10000}
                placeholder="Maintenance notes, receipt or verified replacement record"
              />
            </div>
            <Button disabled={busy || !next}>
              {busy ? "Saving…" : "Save replacement history"}
            </Button>
          </form>
        </DialogContent>
      </Dialog>
      <Dialog
        open={correction !== null}
        onOpenChange={(value) => {
          if (!value && !busy) setCorrection(null);
        }}
      >
        <DialogContent>
          <DialogHeader>
            <DialogTitle>Mark replacement record incorrect</DialogTitle>
            <DialogDescription>
              The original record stays visible with your correction. Add a new
              record separately if needed.
            </DialogDescription>
          </DialogHeader>
          <form
            onSubmit={(event) => {
              event.preventDefault();
              const fields = new FormData(event.currentTarget);
              void submit(`/replacements/${correction!.id}/void`, {
                reason: fields.get("reason"),
              });
            }}
          >
            {error && (
              <p className="error" role="alert">
                {error}
              </p>
            )}
            <div className="field">
              <Label htmlFor="replacement-correction">Correction reason</Label>
              <Input
                id="replacement-correction"
                name="reason"
                maxLength={2000}
                required
              />
            </div>
            <Button disabled={busy}>Save correction</Button>
          </form>
        </DialogContent>
      </Dialog>
    </section>
  );
}
