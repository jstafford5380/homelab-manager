import { SelectField, SelectOption } from "@/components/SelectField";
import { RecoveryHistory } from "./RecoveryHistory";
import { useEffect, useState } from "react";
import { Card } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { Plus, ShieldCheck } from "lucide-react";
type Policy = {
  id: string;
  revision: string;
  asset: string;
  importance: string;
  owner: string;
  destination: string;
  destinationCapacity: string;
  restoreRequirements: string;
  scope: string;
  mechanism: string;
  independence: string;
  failureDomains: string;
  rpoHours: number;
  rtoHours: number;
  exclusions: string;
  schedule: string;
  retention: string;
  runbook: string;
  keyCustodyReference: string;
};
type RecordRow = {
  policy: Policy;
  assessment: {
    protection: string;
    recoveryPoint: string;
    restore: string;
    rto: string;
    representedPointUtc: string | null;
    latestAttempt: string;
  };
  runs: {
    id: string;
    attemptedUtc: string;
    outcome: string;
    author: string;
    reviewByUtc: string;
    evidence: string;
  }[];
  exercises: {
    id: string;
    testedUtc: string;
    result: string;
    scope: string;
    checks: string;
    durationMinutes: number;
    author: string;
    reviewByUtc: string;
    evidence: string;
  }[];
};
function Field({
  name,
  label,
  type = "text",
  value = "",
  required = true,
}: {
  name: string;
  label: string;
  type?: string;
  value?: string | number;
  required?: boolean;
}) {
  return (
    <div className="field">
      <Label htmlFor={"recovery-" + name}>{label}</Label>
      <Input
        id={"recovery-" + name}
        name={name}
        type={type}
        defaultValue={value}
        required={required}
      />
    </div>
  );
}
function Select({
  name,
  label,
  values,
  value,
}: {
  name: string;
  label: string;
  values: string[];
  value?: string;
}) {
  return (
    <div className="field">
      <Label htmlFor={"recovery-" + name}>{label}</Label>
      <SelectField id={"recovery-" + name} name={name} defaultValue={value}>
        {values.map((v) => (
          <SelectOption key={v}>{v}</SelectOption>
        ))}
      </SelectField>
    </div>
  );
}
export function RecoveryView({
  timezone,
  refresh,
}: {
  timezone: string;
  refresh: () => Promise<void>;
}) {
  const [rows, setRows] = useState<RecordRow[]>([]);
  const [modal, setModal] = useState<{
    mode: "policy" | "runs" | "exercises";
    policy?: Policy;
  } | null>(null);
  const [message, setMessage] = useState("");
  const [busy, setBusy] = useState(false);
  async function load() {
    const response = await fetch("/api/v1/recovery");
    if (!response.ok) throw new Error("Could not load recovery evidence");
    setRows(await response.json());
  }
  useEffect(() => {
    void load().catch((e) => setMessage(String(e)));
  }, []);
  const time = (value: string) =>
    new Date(value).toLocaleString(undefined, { timeZone: timezone });
  async function save(fields: FormData) {
    if (!modal) return;
    setBusy(true);
    setMessage("");
    const values: Record<string, unknown> = Object.fromEntries(fields);
    for (const name of [
      "attemptedUtc",
      "recoveryPointUtc",
      "testedUtc",
      "reviewByUtc",
    ])
      if (name in values)
        values[name] = values[name]
          ? new Date(String(values[name])).toISOString()
          : null;
    for (const name of ["rpoHours", "rtoHours", "durationMinutes"])
      if (name in values) values[name] = Number(values[name]);
    if (modal.mode === "runs")
      values.destinationConfirmed = values.destinationConfirmed === "on";
    if (modal.policy)
      values[modal.mode === "policy" ? "revision" : "policyRevision"] = modal.policy.revision;
    const path =
      "/api/v1/recovery" +
      (modal.policy ? "/" + modal.policy.id : "") +
      (modal.mode === "policy" ? "" : "/" + modal.mode);
    try {
      const response = await fetch(path, {
        method: modal.mode === "policy" && modal.policy ? "PUT" : "POST",
        headers: {
          "Content-Type": "application/json",
          "X-Homelab-Request": "1",
        },
        body: JSON.stringify(values),
      });
      if (!response.ok) {
        const error = await response.json();
        if (response.status === 409) {
          try { await load(); }
          catch { throw new Error("The policy changed and its current version could not be loaded. Your draft is still here. Copy any notes you need, then reload Recovery before trying again."); }
        }
        throw new Error(error.error || "Could not save recovery evidence");
      }
      await load();
      await refresh();
      setModal(null);
    } catch (e) {
      setMessage(e instanceof Error ? e.message : String(e));
    } finally {
      setBusy(false);
    }
  }
  return (
    <>
      <div className="section-title">
        <h2>Protection & demonstrated recovery</h2>
        <Button onClick={() => setModal({ mode: "policy" })}>
          <Plus size={15} /> Add protection policy
        </Button>
      </div>
      <p className="muted" style={{ marginBottom: 24 }}>
        RPO uses the recovery point represented at the destination. RTO stays a
        target until a scoped restore exercise measures it. All entries record
        evidence; this application never runs backup or restore operations on
        the NAS.
      </p>
      {message && !modal && (
        <p className="error" role="alert">
          {message}
        </p>
      )}
      {rows.length === 0 && (
        <Card className="empty">
          <ShieldCheck />
          <h3>Can you recover what matters?</h3>
          <p>
            Start with an important dataset or service, its protection
            mechanism, and a destination.
          </p>
        </Card>
      )}
      {rows.map((row) => (
        <Card
          key={row.policy.id}
          className="list-card"
          style={{ marginBottom: 24 }}
        >
          <div className="row">
            <ShieldCheck />
            <div>
              <h2>{row.policy.asset}</h2>
              <small>
                {row.policy.mechanism} → {row.policy.destination}
              </small>
            </div>
            <Badge variant="outline">{row.assessment.protection}</Badge>
          </div>
          <p className="muted" style={{ overflowWrap: "anywhere" }}>
            Importance: {row.policy.importance || "Not recorded"} · Recorded by: {row.policy.owner || "Unknown"}
          </p>
          <div className="coverage-grid">
            <div>
              <strong>Recovery point</strong>
              <Badge variant="outline">{row.assessment.recoveryPoint}</Badge>
              <p className="muted">
                Target: {row.policy.rpoHours} hours ·{" "}
                {row.assessment.representedPointUtc
                  ? time(row.assessment.representedPointUtc)
                  : "Destination not verified"}
              </p>
            </div>
            <div>
              <strong>Restore evidence</strong>
              <Badge variant="outline">{row.assessment.restore}</Badge>
              <p className="muted">Scope: {row.policy.scope}</p>
            </div>
            <div>
              <strong>Recovery time</strong>
              <Badge variant="outline">{row.assessment.rto}</Badge>
              <p className="muted">Target: {row.policy.rtoHours} hours</p>
            </div>
            <div>
              <strong>Latest attempt</strong>
              <Badge variant="outline">{row.assessment.latestAttempt}</Badge>
              <p className="muted">
                Failure domains: {row.policy.failureDomains}
              </p>
            </div>
          </div>
          <div
            style={{
              display: "flex",
              gap: 10,
              flexWrap: "wrap",
              padding: "12px 0",
            }}
          >
            <Button
              variant="outline"
              onClick={() => setModal({ mode: "runs", policy: row.policy })}
            >
              Record backup outcome
            </Button>
            <Button
              variant="outline"
              onClick={() =>
                setModal({ mode: "exercises", policy: row.policy })
              }
            >
              Record restore exercise
            </Button>
            <Button
              variant="outline"
              onClick={() => setModal({ mode: "policy", policy: row.policy })}
            >
              Edit policy
            </Button>
          </div>
          <details style={{ padding: "16px 0" }}>
            <summary>Runbook & evidence history</summary>
            <p style={{ whiteSpace: "pre-wrap", margin: "14px 0" }}>
              {row.policy.runbook || "No recovery runbook recorded."}
            </p>
            <p style={{ whiteSpace: "pre-wrap", overflowWrap: "anywhere" }}>
              Destination capacity (manual): {row.policy.destinationCapacity || "Not recorded"}
            </p>
            <p style={{ whiteSpace: "pre-wrap", overflowWrap: "anywhere" }}>
              Restore prerequisites: {row.policy.restoreRequirements || "Not recorded"}
            </p>
            <p className="muted">These planning notes do not verify current free space or a successful restore.</p>
            <p className="muted">
              Key-custody reference:{" "}
              {row.policy.keyCustodyReference || "Not recorded"} · Store
              references only, never encryption keys.
            </p>
            <p className="muted">Latest three backup records</p>
            {row.runs.slice(0,3).map((run) => (
              <div className="row" key={run.id}>
                <div>
                  <h3>
                    Backup {run.outcome} · {time(run.attemptedUtc)}
                  </h3>
                  <p>{run.evidence}</p>
                  <small>
                    {run.author} · Review by {time(run.reviewByUtc)}
                  </small>
                </div>
              </div>
            ))}
            <p className="muted">Latest three restore exercises</p>
            {row.exercises.slice(0,3).map((exercise) => (
              <div className="row" key={exercise.id}>
                <div>
                  <h3>
                    Restore {exercise.result} · {time(exercise.testedUtc)}
                  </h3>
                  <p>
                    {exercise.scope} · {exercise.durationMinutes} minutes ·{" "}
                    {exercise.checks}
                  </p>
                  <p>{exercise.evidence}</p>
                  <small>
                    {exercise.author} · Review by {time(exercise.reviewByUtc)}
                  </small>
                </div>
              </div>
            ))}
            <RecoveryHistory policyId={row.policy.id} revision={row.policy.revision} asset={row.policy.asset} />
          </details>
        </Card>
      ))}
      <Dialog
        open={!!modal}
        onOpenChange={(open) => {
          if (!open) { setModal(null); setMessage(""); }
        }}
      >
        <DialogContent
          style={{ maxHeight: "85vh", overflow: "auto", maxWidth: 680 }}
        >
          <DialogHeader>
            <DialogTitle>
              {modal?.mode === "policy"
                ? "Protection policy"
                : modal?.mode === "runs"
                  ? "Record existing backup evidence"
                  : "Record a completed restore exercise"}
            </DialogTitle>
          </DialogHeader>
          {modal && (
            <form
              onSubmit={(e) => {
                e.preventDefault();
                void save(new FormData(e.currentTarget));
              }}
            >
              {modal.mode !== "policy" && modal.policy && (
                <p className="muted" style={{ overflowWrap: "anywhere" }}>
                  Recording evidence for {modal.policy.asset}: {modal.policy.scope} → {modal.policy.destination}.
                  Confirm that your evidence covers this policy's current scope and destination.
                </p>
              )}
              {modal.mode === "policy" ? (
                <>
                  <p className="muted">
                    Changing a policy creates a new revision. Earlier evidence
                    stays in history and must be revalidated for the revised
                    policy.
                  </p>
                  <div className="settings-grid">
                    <Field
                      name="asset"
                      label="Dataset or service"
                      value={modal.policy?.asset}
                    />
                    <Select
                      name="importance"
                      label="Recovery importance"
                      values={Array.from(new Set(["low", "normal", "important", "critical", ...(modal.policy?.importance ? [modal.policy.importance] : [])]))}
                      value={modal.policy?.importance || "important"}
                    />
                    <Field
                      name="destination"
                      label="Destination"
                      value={modal.policy?.destination}
                    />
                    <Field
                      name="scope"
                      label="Protected scope"
                      value={modal.policy?.scope}
                    />
                    <Select
                      name="mechanism"
                      label="Protection mechanism"
                      values={[
                        "independent backup",
                        "replication",
                        "local snapshot",
                        "offline/off-site copy",
                      ]}
                      value={modal.policy?.mechanism}
                    />
                    <Select
                      name="independence"
                      label="Destination independence"
                      values={["unverified", "shared", "verified"]}
                      value={modal.policy?.independence}
                    />
                    <Field
                      name="failureDomains"
                      label="Shared pool/power/building/credentials"
                      value={modal.policy?.failureDomains || "unknown"}
                    />
                    <Field
                      name="rpoHours"
                      label="RPO target (hours)"
                      type="number"
                      value={modal.policy?.rpoHours || 24}
                    />
                    <Field
                      name="rtoHours"
                      label="RTO target (hours)"
                      type="number"
                      value={modal.policy?.rtoHours || 4}
                    />
                    <Field
                      name="schedule"
                      label="Schedule"
                      required={false}
                      value={modal.policy?.schedule}
                    />
                    <Field
                      name="retention"
                      label="Retention"
                      required={false}
                      value={modal.policy?.retention}
                    />
                    <Field
                      name="exclusions"
                      label="Exclusions"
                      required={false}
                      value={modal.policy?.exclusions}
                    />
                    <Field
                      name="keyCustodyReference"
                      label="Key custody reference (no secrets)"
                      required={false}
                      value={modal.policy?.keyCustodyReference}
                    />
                  </div>
                  <div className="field">
                    <Label htmlFor="recovery-destinationCapacity">Destination capacity notes (optional)</Label>
                    <textarea id="recovery-destinationCapacity" name="destinationCapacity" rows={2} maxLength={4000}
                      aria-describedby="capacity-help" defaultValue={modal.policy?.destinationCapacity} />
                    <p id="capacity-help" className="muted">Record total/free space, units, date checked and evidence source. Leave blank if unknown; this is not a live capacity check.</p>
                  </div>
                  <div className="field">
                    <Label htmlFor="recovery-restoreRequirements">Restore prerequisites (optional)</Label>
                    <textarea id="recovery-restoreRequirements" name="restoreRequirements" rows={3} maxLength={10000}
                      aria-describedby="prerequisites-help" defaultValue={modal.policy?.restoreRequirements} />
                    <p id="prerequisites-help" className="muted">Include configuration backups, permissions, application state and dependencies. Use secure custody references for secrets.</p>
                  </div>
                  <div className="field">
                    <Label htmlFor="runbook">Recovery runbook</Label>
                    <textarea
                      id="runbook"
                      name="runbook"
                      rows={5}
                      defaultValue={modal.policy?.runbook}
                    />
                  </div>
                </>
              ) : modal.mode === "runs" ? (
                <>
                  <Select
                    name="outcome"
                    label="Outcome"
                    values={["success", "failed", "unknown"]}
                  />
                  <Field
                    name="attemptedUtc"
                    label="Attempt finished at (browser timezone)"
                    type="datetime-local"
                  />
                  <Field
                    name="recoveryPointUtc"
                    label="Recovery point represented at destination"
                    type="datetime-local"
                    required={false}
                  />
                  <label className="backup-checkbox">
                    <input name="destinationConfirmed" type="checkbox" />{" "}
                    Destination evidence confirms this recovery point
                  </label>
                  <Field
                    name="failureReason"
                    label="Failure reason"
                    required={false}
                  />
                  <Field name="evidence" label="Evidence / reference" />
                  <Field
                    name="reviewByUtc"
                    label="Review this manual evidence by"
                    type="datetime-local"
                  />
                </>
              ) : (
                <>
                  <Field
                    name="testedUtc"
                    label="Test completed at (browser timezone)"
                    type="datetime-local"
                  />
                  <Field
                    name="recoveryPointUtc"
                    label="Recovery point restored"
                    type="datetime-local"
                  />
                  <Field
                    name="scope"
                    label="Scope actually restored"
                    value={modal.policy?.scope}
                  />
                  <Field
                    name="checks"
                    label="Integrity / application checks performed"
                  />
                  <Select
                    name="result"
                    label="Result"
                    values={["passed", "failed", "aborted"]}
                  />
                  <Field
                    name="durationMinutes"
                    label="Measured recovery duration (minutes)"
                    type="number"
                  />
                  <Field name="evidence" label="Evidence / reference" />
                  <Field
                    name="reviewByUtc"
                    label="Review this restore evidence by"
                    type="datetime-local"
                  />
                </>
              )}
              {message && (
                <p className="error" role="alert">
                  {message}
                </p>
              )}
              <Button disabled={busy}>Save evidence</Button>
            </form>
          )}
        </DialogContent>
      </Dialog>
    </>
  );
}
