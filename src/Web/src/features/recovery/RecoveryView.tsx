import { RecoveryEditor } from "./RecoveryEditor";
import { RecoveryHistory } from "./RecoveryHistory";
import { useEffect, useState } from "react";
import { Card } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Plus, ShieldCheck } from "lucide-react";
import type { Policy, RecordRow } from "./recoveryModel";

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
      values[modal.mode === "policy" ? "revision" : "policyRevision"] =
        modal.policy.revision;
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
          try {
            await load();
          } catch {
            throw new Error(
              "The policy changed and its current version could not be loaded. Your draft is still here. Copy any notes you need, then reload Recovery before trying again.",
            );
          }
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
            Importance: {row.policy.importance || "Not recorded"} · Recorded by:{" "}
            {row.policy.owner || "Unknown"}
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
              Destination capacity (manual):{" "}
              {row.policy.destinationCapacity || "Not recorded"}
            </p>
            <p style={{ whiteSpace: "pre-wrap", overflowWrap: "anywhere" }}>
              Restore prerequisites:{" "}
              {row.policy.restoreRequirements || "Not recorded"}
            </p>
            <p className="muted">
              These planning notes do not verify current free space or a
              successful restore.
            </p>
            <p className="muted">
              Key-custody reference:{" "}
              {row.policy.keyCustodyReference || "Not recorded"} · Store
              references only, never encryption keys.
            </p>
            <p className="muted">Latest three backup records</p>
            {row.runs.slice(0, 3).map((run) => (
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
            {row.exercises.slice(0, 3).map((exercise) => (
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
            <RecoveryHistory
              policyId={row.policy.id}
              revision={row.policy.revision}
              asset={row.policy.asset}
            />
          </details>
        </Card>
      ))}
      <RecoveryEditor
        modal={modal}
        busy={busy}
        message={message}
        onClose={() => {
          setModal(null);
          setMessage("");
        }}
        save={save}
      />
    </>
  );
}
