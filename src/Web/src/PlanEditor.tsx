import { ReasonChips } from "./ReasonChips";
import { Pencil } from "lucide-react";
import { type PlanningReason } from "./planning";
import { SelectField, SelectOption } from "@/components/SelectField";
import { useDisplayFormat } from "./displayPreferences";
import { useEffect, useState } from "react";
import { Link } from "react-router";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Textarea } from "@/components/ui/textarea";
import { Label } from "@/components/ui/label";
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogDescription,
} from "@/components/ui/dialog";

export type Plan = {
  id: string;
  revision: string;
  diskId: string | null;
  title: string;
  reason: string;
  notes?: string;
  evidence?: PlanningReason[];
  completedUtc?: string | null;
  owner: string;
  dueUtc: string | null;
  budget: number | null;
  currency: string;
  status: string;
};
type Change = {
  id: number;
  atUtc: string;
  actor: string;
  before: Plan | null;
  after: Plan;
};
type History = { items: Change[]; nextId: number | null };
const money = (plan: Plan) =>
  plan.budget === null
    ? "Not set"
    : `${plan.budget.toLocaleString()} ${plan.currency}`;
function changes(entry: Change, time: (value: string | null) => string) {
  const before = entry.before,
    after = entry.after;
  if (!before)
    return [
      `Created: ${after.title}`,
      `Owner: ${after.owner || "Unassigned"}`,
      `Budget: ${money(after)}`,
      `Review by: ${time(after.dueUtc)}`,
      `Status: ${after.status}`,
      `Notes: ${after.notes ?? after.reason ?? "None"}`,
    ];
  const result: string[] = [];
  if (before.title !== after.title)
    result.push(`Title: ${before.title} → ${after.title}`);
  if (before.owner !== after.owner)
    result.push(
      `Owner: ${before.owner || "Unassigned"} → ${after.owner || "Unassigned"}`,
    );
  if (before.budget !== after.budget || before.currency !== after.currency)
    result.push(`Budget: ${money(before)} → ${money(after)}`);
  if (before.status !== after.status)
    result.push(`Status: ${before.status} → ${after.status}`);
  if (before.dueUtc !== after.dueUtc)
    result.push(`Review by: ${time(before.dueUtc)} → ${time(after.dueUtc)}`);
  if (before.reason !== after.reason)
    result.push(
      `Previous evidence: ${before.reason || "None"}`,
      `Updated evidence: ${after.reason || "None"}`,
    );
  return result.length
    ? result
    : ["Saved without changing the planning fields."];
}
export function PlanEditor({
  plan,
  onSaved,
  addEstimate = false,
}: {
  addEstimate?: boolean;
  plan: Plan;
  onSaved: (saved?: Plan) => void | Promise<void>;
}) {
  const { time, timezone } = useDisplayFormat();
  const [open, setOpen] = useState(false);
  const [draft, setDraft] = useState(plan);
  const [due, setDue] = useState("");
  const [budget, setBudget] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [history, setHistory] = useState<History | null>(null);
  const [historyError, setHistoryError] = useState("");
  const [before, setBefore] = useState<number | null>(null);
  const [retry, setRetry] = useState(0);
  function show(value: boolean) {
    if (busy) return;
    if (value) {
      setDraft({ ...plan });
      setDue(
        plan.dueUtc ? new Date(plan.dueUtc).toISOString().slice(0, 19) : "",
      );
      setBudget(plan.budget?.toString() ?? "");
      setError("");
      setHistory(null);
      setBefore(null);
    }
    setOpen(value);
  }
  useEffect(() => {
    if (!open) return;
    const controller = new AbortController();
    const timeout = setTimeout(() => controller.abort("timeout"), 15000);
    setHistory(null);
    setHistoryError("");
    void fetch(
      `/api/v1/plans/${plan.id}/history${before === null ? "" : `?before=${before}`}`,
      { signal: controller.signal },
    )
      .then(async (response) => {
        if (!response.ok) throw new Error();
        const data: History = await response.json();
        if (!Array.isArray(data.items)) throw new Error();
        if (!controller.signal.aborted) setHistory(data);
      })
      .catch(() => {
        if (
          !controller.signal.aborted ||
          controller.signal.reason === "timeout"
        )
          setHistoryError("Change history is unavailable.");
      })
      .finally(() => clearTimeout(timeout));
    return () => {
      clearTimeout(timeout);
      controller.abort();
    };
  }, [open, plan.id, before, retry]);
  async function save(event: React.FormEvent) {
    event.preventDefault();
    setBusy(true);
    setError("");
    try {
      const response = await fetch(`/api/v1/plans/${draft.id}`, {
        method: "PUT",
        headers: {
          "Content-Type": "application/json",
          "X-Homelab-Request": "1",
        },
        body: JSON.stringify({
          ...draft,
          notes: draft.reason,
          budget: budget === "" ? null : Number(budget),
          dueUtc: due ? new Date(due + "Z").toISOString() : null,
        }),
      });
      if (!response.ok) {
        const result = await response.json();
        if (response.status === 409) await onSaved();
        throw new Error(result.error || "Could not save the plan.");
      }
      await onSaved(await response.json());
      setOpen(false);
    } catch (e) {
      setError(e instanceof Error ? e.message : "Could not save the plan.");
    } finally {
      setBusy(false);
    }
  }
  return (
    <>
      <Button
        variant="ghost"
        className={
          addEstimate ? "planning-chip tone-warn dashed" : "planning-edit"
        }
        onClick={() => show(true)}
        aria-label={
          addEstimate ? `Add estimate for ${plan.title}` : `Edit ${plan.title}`
        }
      >
        {addEstimate ? "Add estimate" : <Pencil size={15} />}
      </Button>
      <Dialog open={open} onOpenChange={show}>
        <DialogContent
          style={{
            width: "min(720px, calc(100vw - 2rem))",
            maxHeight: "85vh",
            overflowY: "auto",
          }}
        >
          <DialogHeader>
            <DialogTitle>Edit plan</DialogTitle>
            <DialogDescription>
              Update your local plan and preserve its change history. This does
              not purchase hardware, contact the owner, or change the NAS.
            </DialogDescription>
          </DialogHeader>
          <form onSubmit={save}>
            <div className="field">
              <Label htmlFor={`plan-title-${plan.id}`}>Plan title</Label>
              <Input
                id={`plan-title-${plan.id}`}
                required
                maxLength={200}
                value={draft.title}
                onChange={(e) => setDraft({ ...draft, title: e.target.value })}
              />
            </div>
            <div className="field">
              <Label htmlFor={`plan-owner-${plan.id}`}>Plan owner</Label>
              <Input
                id={`plan-owner-${plan.id}`}
                maxLength={200}
                value={draft.owner}
                placeholder="Unassigned"
                onChange={(e) => setDraft({ ...draft, owner: e.target.value })}
              />
            </div>
            <div className="settings-grid">
              <div className="field">
                <Label htmlFor={`plan-budget-${plan.id}`}>
                  Estimated budget
                </Label>
                <Input
                  id={`plan-budget-${plan.id}`}
                  type="number"
                  min={0}
                  max={1000000000}
                  step="0.01"
                  value={budget}
                  onChange={(e) => setBudget(e.target.value)}
                />
              </div>
              <div className="field">
                <Label htmlFor={`plan-currency-${plan.id}`}>
                  Budget currency
                </Label>
                <Input
                  id={`plan-currency-${plan.id}`}
                  required
                  pattern="[A-Z]{3}"
                  maxLength={3}
                  value={draft.currency}
                  onChange={(e) =>
                    setDraft({
                      ...draft,
                      currency: e.target.value.toUpperCase(),
                    })
                  }
                />
              </div>
              <div className="field">
                <Label htmlFor={`plan-due-${plan.id}`}>Review by (UTC)</Label>
                <Input
                  id={`plan-due-${plan.id}`}
                  type="datetime-local"
                  step="1"
                  min="1970-01-01T00:00"
                  value={due}
                  onChange={(e) => setDue(e.target.value)}
                />
              </div>
              <div className="field">
                <Label htmlFor={`plan-status-${plan.id}`}>Plan status</Label>
                <SelectField
                  id={`plan-status-${plan.id}`}
                  value={draft.status}
                  onValueChange={(e) => setDraft({ ...draft, status: e })}
                >
                  <SelectOption value="planned">Planned</SelectOption>
                  <SelectOption value="ordered">Ordered</SelectOption>
                  <SelectOption value="complete">Complete</SelectOption>
                </SelectField>
              </div>
            </div>
            <div className="field">
              <Label htmlFor={`plan-reason-${plan.id}`}>Notes</Label>
              <Textarea
                id={`plan-reason-${plan.id}`}
                rows={5}
                maxLength={20000}
                value={draft.reason ?? draft.notes ?? ""}
                onChange={(e) => setDraft({ ...draft, reason: e.target.value })}
              />
            </div>
            {!!draft.evidence?.length && (
              <section aria-label="Evidence attached">
                <h3>Evidence attached</h3>
                <ReasonChips reasons={draft.evidence} timezone={timezone} />
              </section>
            )}
            {draft.diskId && (
              <p className="muted">
                Linked to a fixed physical drive.{" "}
                <Link to={`/storage?disk=${draft.diskId}`}>
                  View drive evidence
                </Link>
              </p>
            )}
            {error && (
              <p role="alert" className="error">
                {error}
              </p>
            )}
            <div style={{ display: "flex", gap: 12, margin: "16px 0" }}>
              <Button disabled={busy}>
                {busy ? "Saving…" : "Save plan changes"}
              </Button>
              <Button
                type="button"
                variant="outline"
                disabled={busy}
                onClick={() => show(false)}
              >
                Cancel
              </Button>
            </div>
          </form>
          <section
            className="plan-change-history"
            aria-label="Plan change history"
          >
            <h3>Plan change history</h3>
            {historyError && (
              <p role="alert">
                {historyError}{" "}
                <Button variant="ghost" onClick={() => setRetry((n) => n + 1)}>
                  Retry history
                </Button>
              </p>
            )}
            {!history && !historyError && (
              <p className="muted">Loading history…</p>
            )}
            {history && !history.items.length && (
              <p className="muted">
                No recorded changes yet. Earlier plans may predate change
                tracking.
              </p>
            )}
            {history?.items.map((entry) => (
              <article
                key={entry.id}
                style={{
                  marginTop: 16,
                  paddingTop: 12,
                  borderTop: "1px solid var(--border)",
                }}
              >
                <strong>{entry.actor}</strong>
                <p className="muted">{time(entry.atUtc)}</p>
                <ul style={{ paddingLeft: 20, listStyle: "disc" }}>
                  {changes(entry, time).map((line, index) => (
                    <li
                      key={index}
                      style={{
                        whiteSpace: "pre-wrap",
                        overflowWrap: "anywhere",
                        marginTop: 8,
                      }}
                    >
                      {line}
                    </li>
                  ))}
                </ul>
              </article>
            ))}
            <div style={{ display: "flex", gap: 8, marginTop: 12 }}>
              {before !== null && (
                <Button variant="outline" onClick={() => setBefore(null)}>
                  Latest changes
                </Button>
              )}
              {history?.nextId != null && (
                <Button
                  variant="outline"
                  onClick={() => setBefore(history.nextId)}
                >
                  Older changes
                </Button>
              )}
            </div>
          </section>
        </DialogContent>
      </Dialog>
    </>
  );
}
