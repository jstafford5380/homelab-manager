import type {Plan} from "./planning";
import {ReasonChips} from "./ReasonChips";
import {useState} from "react";
import {Button} from "@/components/ui/button";
import {Input} from "@/components/ui/input";
import {Textarea} from "@/components/ui/textarea";
import {Label} from "@/components/ui/label";
import {Dialog,DialogContent,DialogDescription,DialogHeader,DialogTitle} from "@/components/ui/dialog";
import {usePlanningClock,calendarDay,priorities,priority,type DriveReview} from "./planning";

type Price = {
  name: string;
  estimatedUnitCost: number | null;
  currency: string;
};
function noonInZone(date: string, timezone: string) {
  const target = Date.parse(`${date}T12:00:00Z`);
  let candidate = target;
  for (let i = 0; i < 3; i++) {
    const parts = new Intl.DateTimeFormat("en-CA", {
      timeZone: timezone,
      year: "numeric",
      month: "2-digit",
      day: "2-digit",
      hour: "2-digit",
      minute: "2-digit",
      hourCycle: "h23",
    }).formatToParts(candidate);
    const get = (key: string) =>
      Number(parts.find((p) => p.type === key)!.value);
    const displayed = Date.UTC(
      get("year"),
      get("month") - 1,
      get("day"),
      get("hour"),
      get("minute"),
    );
    candidate += target - displayed;
  }
  return new Date(candidate).toISOString();
}
export function CreatePlanDialog({
  open,
  onOpenChange,
  review,
  price,
  timezone,
  onChanged,
  initialTitle = "",
  initialNotes = "",
}: {
  open: boolean;
  onOpenChange: (value: boolean) => void;
  review: DriveReview | null;
  price: Price | null;
  timezone: string;
  onChanged: (saved?: Plan) => Promise<void>;
  initialTitle?: string;
  initialNotes?: string;
}) {
  const [busy, setBusy] = useState(false),
    [error, setError] = useState("");
  const now = usePlanningClock();
  const key = review ? (priority(review) as keyof typeof priorities) : null;
  const title = key ? priorities[key].action : "Add plan";
  const defaultDue =
    key && key !== "prepare"
      ? new Date((calendarDay(now, timezone) + 14) * 86400000)
          .toISOString()
          .slice(0, 10)
      : "";
  return (
    <Dialog
      open={open}
      onOpenChange={(value) => {
        if (!busy) {
          setError("");
          onOpenChange(value);
        }
      }}
    >
      <DialogContent className="planning-dialog">
        <DialogHeader>
          <DialogTitle>{title}</DialogTitle>
          <DialogDescription>
            Saved in Homelab Control. Nothing is ordered and the NAS isn’t
            changed.
          </DialogDescription>
        </DialogHeader>
        <form
          onSubmit={async (event) => {
            event.preventDefault();
            const fields = new FormData(event.currentTarget);
            setBusy(true);
            setError("");
            try {
              const response = await fetch("/api/v1/plans", {
                method: "POST",
                signal: AbortSignal.timeout(15000),
                headers: {
                  "Content-Type": "application/json",
                  "X-Homelab-Request": "1",
                },
                body: JSON.stringify({
                  diskId: review?.diskId ?? null,
                  title: fields.get("title"),
                  notes: fields.get("notes"),
                  evidence: review?.reasons ?? [],
                  budget: fields.get("budget")
                    ? Number(fields.get("budget"))
                    : null,
                  currency: fields.get("currency"),
                  owner: fields.get("owner"),
                  dueUtc: fields.get("due")
                    ? noonInZone(String(fields.get("due")), timezone)
                    : null,
                }),
              });
              if (!response.ok) {
                const body = await response.json().catch(() => ({}));
                throw new Error(body.error || "Plan could not be saved.");
              }
              onOpenChange(false);
              await onChanged(await response.json());
            } catch (e) {
              setError(
                e instanceof Error ? e.message : "Plan could not be saved.",
              );
            } finally {
              setBusy(false);
            }
          }}
        >
          <div className="field">
            <Label htmlFor="create-plan-title">Title</Label>
            <Input
              id="create-plan-title"
              name="title"
              required
              maxLength={200}
              defaultValue={
                review
                  ? key === "media"
                    ? `Replace ${review.serial}`
                    : `Review ${review.label || review.model} ${review.serial}`
                  : initialTitle
              }
            />
          </div>
          {review && (
            <section
              className="planning-attached"
              aria-label="Evidence attached"
            >
              <h3>
                Evidence attached{" "}
                <small>
                  · {review.label || review.model} {review.serial}
                </small>
              </h3>
              <ReasonChips
                reasons={review.reasons}
                tone={key ? priorities[key].tone : "info"}
                timezone={timezone}
              />
            </section>
          )}
          <div className="field">
            <Label htmlFor="create-plan-notes">Notes</Label>
            <Textarea
              id="create-plan-notes"
              name="notes"
              rows={3}
              maxLength={20000}
              defaultValue={initialNotes}
              placeholder="What you’ll check, and what would change the plan"
            />
          </div>
          <div className="planning-form-grid">
            <div className="field">
              <Label htmlFor="create-plan-budget">Estimate (optional)</Label>
              <Input
                id="create-plan-budget"
                name="budget"
                type="number"
                min="0"
                max="1000000000"
                step="0.01"
                defaultValue={price?.estimatedUnitCost ?? ""}
              />
              <small>
                {price?.estimatedUnitCost != null
                  ? `From your Inventory price for ${price.name}`
                  : "Check a spare to fill in its Inventory price."}
              </small>
            </div>
            <div className="field">
              <Label htmlFor="create-plan-due">Due (optional)</Label>
              <Input
                id="create-plan-due"
                name="due"
                type="date"
                min="1970-01-01"
                defaultValue={defaultDue}
              />
              {defaultDue && <small>Two weeks from today</small>}
            </div>
            <div className="field">
              <Label htmlFor="create-plan-currency">Currency</Label>
              <Input
                id="create-plan-currency"
                name="currency"
                required
                pattern="[A-Z]{3}"
                maxLength={3}
                defaultValue={price?.currency || "USD"}
              />
            </div>
            <div className="field">
              <Label htmlFor="create-plan-owner">Owner (optional)</Label>
              <Input
                id="create-plan-owner"
                name="owner"
                maxLength={200}
                placeholder="Unassigned"
              />
            </div>
          </div>
          {error && (
            <p role="alert" className="error">
              {error}
            </p>
          )}
          <div className="planning-dialog-actions">
            <Button
              type="button"
              variant="outline"
              disabled={busy}
              onClick={() => onOpenChange(false)}
            >
              Cancel
            </Button>
            <Button disabled={busy}>{busy ? "Saving…" : "Save plan"}</Button>
          </div>
        </form>
      </DialogContent>
    </Dialog>
  );
}
