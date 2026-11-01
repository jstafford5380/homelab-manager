import { Link } from "react-router";
import { lazy, Suspense, useEffect, useState } from "react";
import { Card } from "@/components/ui/card";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogDescription,
} from "@/components/ui/dialog";
import { ClipboardList, Package } from "lucide-react";

const SpareReview = lazy(() => import("./SpareReview"));

type Review = {
  diskId: string;
  model: string;
  serial: string;
  priority: string;
  nextStep: string;
  lastSeenUtc: string;
  present: boolean;
  mappingConfidence: string;
  reasons: { kind: string; detail: string; findingId: string | null }[];
  openPlanIds: string[];
};
type Data = {
  generatedUtc: string;
  ruleVersion: string;
  trackedDriveCount: number;
  driveAgeReviewYears: number;
  warrantyNoticeDays: number;
  reviews: Review[];
  spares: {
    id: string;
    name: string;
    quantity: number;
    updatedUtc: string;
    compatibilityNotes: string;
    verification: string;
    estimatedUnitCost: number | null;
    currency: string;
  }[];
  budgets: {
    currency: string;
    total: number;
    withoutEstimate: number;
    count: number;
  }[];
};
const money = (value: number, currency: string) =>
  new Intl.NumberFormat(undefined, { style: "currency", currency }).format(
    value,
  );
export function PlanningReview({
  timezone,
  onChanged,
  planRevision,
}: {
  timezone: string;
  onChanged: () => Promise<void>;
  planRevision: string;
}) {
  const [data, setData] = useState<Data | null>(null);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");
  const [selected, setSelected] = useState<Review | null>(null);
  const [busy, setBusy] = useState(false);
  const [all, setAll] = useState(false);
  const [retry, setRetry] = useState(0);
  useEffect(() => {
    let disposed = false;
    let request: AbortController | null = null;
    let refreshTimer: ReturnType<typeof setTimeout> | undefined;
    async function load() {
      const controller = new AbortController();
      request = controller;
      const deadline = setTimeout(() => controller.abort(), 15000);
      try {
        const response = await fetch("/api/v1/planning/review", { signal: controller.signal });
        if (!response.ok) throw new Error("Planning evidence could not be refreshed.");
        const next: Data = await response.json();
        if (!next || !Array.isArray(next.reviews) || !Array.isArray(next.spares) || !Array.isArray(next.budgets) ||
            typeof next.generatedUtc !== "string" || !Number.isFinite(Date.parse(next.generatedUtc)))
          throw new Error("Planning evidence returned an invalid response.");
        if (!disposed && !controller.signal.aborted) {
          setData(next);
          setError("");
        }
      } catch (e) {
        if (!disposed) setError(controller.signal.aborted ? "Planning evidence request timed out." : e instanceof Error ? e.message : "Planning evidence could not be refreshed.");
      } finally {
        clearTimeout(deadline);
        if (!disposed) refreshTimer = setTimeout(() => void load(), 30000);
      }
    }
    void load();
    return () => {
      disposed = true;
      request?.abort();
      clearTimeout(refreshTimer);
    };
  }, [planRevision, retry]);
  return (
    <section
      className="planning-review"
      aria-label="Evidence-based planning review"
    >
      <div className="section-title">
        <h2>Prepare for what’s next</h2>
        <Badge variant="outline">Advice only</Badge>
      </div>
      <p className="muted">
        Start with observed issues, then review records and spare stock. These
        reminders do not predict drive failure.
      </p>
      {error && (
        <div role="alert">
          <p className="error">
            {error} {data && "Showing previously loaded evidence."}
          </p>
          <Button variant="outline" onClick={() => setRetry((x) => x + 1)}>
            Retry planning review
          </Button>
        </div>
      )}
      {notice && <p role="status">{notice}</p>}
      {!data && !error && <p className="muted">Loading planning evidence…</p>}
      {data && (
        <>
          <div className="planning-context">
            <Card className="list-card">
              <h3>
                <Package size={18} /> Spare stock
              </h3>
              {data.spares.length ? (
                data.spares.map((spare) => (
                  <div className="planning-stock" key={spare.id}>
                    <strong>
                      {spare.name} · {spare.quantity} on hand
                    </strong>
                    <p>
                      {spare.compatibilityNotes ||
                        "Compatibility has not been documented."}
                    </p>
                    <small>
                      Inventory: {spare.verification}
                      {spare.estimatedUnitCost !== null &&
                        ` · ${money(spare.estimatedUnitCost, spare.currency)} estimated per unit`}
                    </small>
                  </div>
                ))
              ) : (
                <p>
                  No spare stock recorded. Add stock and compatibility notes in
                  Inventory.
                </p>
              )}
              <p className="muted">
                Stock is not matched to drives automatically. Confirm capacity,
                interface, sector format and physical fit.
              </p>
            </Card>
            <Card className="list-card">
              <h3>
                <ClipboardList size={18} /> Planned & ordered budget
              </h3>
              {data.budgets.length ? (
                data.budgets.map((budget) => (
                  <div key={budget.currency}>
                    <strong className="planning-total">
                      {money(budget.total, budget.currency)}
                    </strong>
                    <p>
                      {budget.count} open plan(s) · {budget.withoutEstimate}{" "}
                      without an estimate
                    </p>
                  </div>
                ))
              ) : (
                <p>No open plans yet.</p>
              )}
              <p className="muted">
                Manual estimates, grouped by currency. Completed plans are
                excluded; this is not a purchase total or live pricing.
              </p>
            </Card>
          </div>
          <div className="section-title">
            <h3>Drive reviews</h3>
            <span className="muted">
              {data.reviews.length} of {data.trackedDriveCount} tracked drives
            </span>
          </div>
          {!data.reviews.length && (
            <Card className="list-card">
              <p className="planning-empty">
                {data.trackedDriveCount
                  ? "No reminders from the current planning rules. Check monitoring coverage and recovery readiness before drawing a health conclusion."
                  : "Drive reviews will appear after inventory is collected. You can still create plans and record spare stock now."}
              </p>
            </Card>
          )}
          {(all ? data.reviews : data.reviews.slice(0, 4)).map((review) => (
            <Card key={review.diskId} className="list-card drive-review">
              <div className="section-title">
                <h3>{review.model || "Unidentified model"}</h3>
                <Badge
                  variant={
                    review.priority === "Review media evidence"
                      ? "destructive"
                      : "outline"
                  }
                >
                  {review.priority}
                </Badge>
              </div>
              <p className="muted">
                Serial {review.serial || "unknown"} ·{" "}
                {review.present
                  ? "Present in last inventory"
                  : "Absent from last inventory"}{" "}
                · Identity {review.mappingConfidence}
              </p>
              <p className="review-next-step">{review.nextStep}</p>
              <details>
                <summary>
                  Why this drive is listed ({review.reasons.length})
                </summary>
                <ul>
                  {review.reasons.map((reason, index) => (
                    <li key={index}>
                      {reason.detail}{" "}
                      {reason.findingId && (
                        <Link to={`/?finding=${reason.findingId}`}>
                          View finding evidence
                        </Link>
                      )}
                    </li>
                  ))}
                </ul>
              </details>
              <Suspense fallback={<p>Loading spare planning…</p>}><SpareReview diskId={review.diskId} spares={data.spares} timezone={timezone} /></Suspense>
              <div className="review-actions">
                <small>
                  Last seen{" "}
                  {new Date(review.lastSeenUtc).toLocaleString(undefined, {
                    timeZone: timezone,
                  })}
                </small>
                <Button
                  variant="outline"
                  disabled={review.openPlanIds.length > 0}
                  onClick={() => {
                    setSelected(review);
                    setNotice("");
                  }}
                >
                  {review.openPlanIds.length
                    ? "Already in your plan"
                    : "Create review plan"}
                </Button>
              </div>
            </Card>
          ))}
          {data.reviews.length > 4 && (
            <Button
              variant="ghost"
              aria-expanded={all}
              onClick={() => setAll(!all)}
            >
              {all
                ? "Show fewer drives"
                : `Show all ${data.reviews.length} drive reviews`}
            </Button>
          )}
          <p className="muted planning-rule-note">
            Review generated {new Date(data.generatedUtc).toLocaleString(undefined, { timeZone: timezone })}. Planning thresholds: {data.driveAgeReviewYears} years since
            purchase, {data.warrantyNoticeDays} days before warranty end. Change
            these in Settings. Rule {data.ruleVersion}.
          </p>
        </>
      )}
      <Dialog
        open={selected !== null}
        onOpenChange={(open) => {
          if (!open && !busy) setSelected(null);
        }}
      >
        <DialogContent>
          <DialogHeader>
            <DialogTitle>Create a drive review plan</DialogTitle>
            <DialogDescription>
              This saves a task in Homelab Control. It does not purchase a drive
              or change the NAS.
            </DialogDescription>
          </DialogHeader>
          {selected && (
            <form
              onSubmit={async (event) => {
                event.preventDefault();
                const fields = new FormData(event.currentTarget);
                setBusy(true);
                setError("");
                try {
                  const response = await fetch("/api/v1/plans", {
                    method: "POST",
                    headers: {
                      "Content-Type": "application/json",
                      "X-Homelab-Request": "1",
                    },
                    body: JSON.stringify({
                      diskId: selected.diskId,
                      title: fields.get("title"),
                      reason: `${fields.get("reason")}\n\nPlanning evidence (${data?.ruleVersion}, reviewed ${new Date().toISOString()}):\n${selected.reasons.map((reason) => reason.detail).join("\n")}`,
                      budget: fields.get("budget")
                        ? Number(fields.get("budget"))
                        : null,
                      currency: fields.get("currency"),
                      dueUtc: fields.get("due")
                        ? `${fields.get("due")}T12:00:00Z`
                        : null,
                    }),
                  });
                  if (!response.ok) {
                    const body = await response.json();
                    throw new Error(body.error || "Plan could not be saved.");
                  }
                  setSelected(null);
                  setRetry((x) => x + 1);
                  setNotice(
                    "Review plan saved. Evidence has been attached to the task.",
                  );
                  await onChanged();
                } catch (e) {
                  setError(e instanceof Error ? e.message : String(e));
                } finally {
                  setBusy(false);
                }
              }}
            >
              {error && (
                <p className="error" role="alert">
                  {error}
                </p>
              )}
              <div className="field">
                <Label htmlFor="review-title">Plan title</Label>
                <Input
                  id="review-title"
                  name="title"
                  maxLength={200}
                  required
                  defaultValue={`Review ${selected.model || "drive"} ${selected.serial}`}
                />
              </div>
              <div className="field">
                <Label htmlFor="review-reason">
                  Next step or budget assumptions
                </Label>
                <Input
                  id="review-reason"
                  name="reason"
                  maxLength={2000}
                  required
                  defaultValue={selected.nextStep}
                />
              </div>
              <div className="settings-grid">
                <div className="field">
                  <Label htmlFor="review-budget">
                    Estimated budget (optional)
                  </Label>
                  <Input
                    id="review-budget"
                    name="budget"
                    type="number"
                    min="0"
                    max="1000000000"
                    step="0.01"
                  />
                </div>
                <div className="field">
                  <Label htmlFor="review-currency">Currency</Label>
                  <Input
                    id="review-currency"
                    name="currency"
                    required
                    pattern="[A-Z]{3}"
                    maxLength={3}
                    defaultValue="USD"
                  />
                </div>
              </div>
              <div className="field">
                <Label htmlFor="review-due">Review by (optional)</Label>
                <Input id="review-due" name="due" type="date" />
              </div>
              <Button disabled={busy}>
                {busy ? "Saving…" : "Save review plan"}
              </Button>
            </form>
          )}
        </DialogContent>
      </Dialog>
    </section>
  );
}
