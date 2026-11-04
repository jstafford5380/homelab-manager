import {SelectField,SelectOption} from "@/components/SelectField";
import {useEffect,useRef,useState} from "react";
import {Button} from "@/components/ui/button";
import {Input} from "@/components/ui/input";
import {Label} from "@/components/ui/label";
import {Badge} from "@/components/ui/badge";
import {Dialog,DialogContent,DialogHeader,DialogTitle,DialogDescription} from "@/components/ui/dialog";

import type {Spare} from "./planning";
type Entry = {
  status: string;
  review: {
    id: number;
    assetName: string;
    verdict: string;
    evidence: string;
    author: string;
    recordedUtc: string;
    validUntilUtc: string;
  };
};
type History = {
  generatedUtc: string;
  items: Entry[];
  nextBefore: number | null;
};
export default function SpareReview({
  diskId,
  spares,
  timezone,
  onChanged,
}: {
  onChanged?: () => void;
  diskId: string;
  spares: Spare[];
  timezone: string;
}) {
  const [now, setNow] = useState(() => Date.now());
  useEffect(() => {
    const timer = setInterval(() => setNow(Date.now()), 10000);
    return () => clearInterval(timer);
  }, []);
  const [history, setHistory] = useState<History | null>(null);
  const [before, setBefore] = useState<number | null>(null);
  const [retry, setRetry] = useState(0);
  const [error, setError] = useState("");
  const [open, setOpen] = useState(false);
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState("");
  const [requestId, setRequestId] = useState("");
  const [choices, setChoices] = useState<Spare[]>([]);
  const messageRef = useRef<HTMLParagraphElement>(null);
  useEffect(() => {
    if (message && open) messageRef.current?.focus();
  }, [message, open]);
  useEffect(() => {
    if (!open) return;
    const controller = new AbortController();
    let pending = false;
    async function load() {
      if (pending) return;
      pending = true;
      try {
        const response = await fetch(
          `/api/v1/disks/${diskId}/spare-reviews${before ? `?before=${before}` : ""}`,
          {
            signal: AbortSignal.any([
              controller.signal,
              AbortSignal.timeout(15000),
            ]),
          },
        );
        if (!response.ok)
          throw new Error("Spare reviews could not be refreshed.");
        const body: History = await response.json();
        if (
          !Number.isFinite(Date.parse(body.generatedUtc)) ||
          !Array.isArray(body.items) ||
          body.items.length > 25 ||
          body.items.some(
            (x) =>
              !x?.review ||
              !Number.isSafeInteger(x.review.id) ||
              [
                x.status,
                x.review.assetName,
                x.review.verdict,
                x.review.evidence,
                x.review.author,
              ].some((v) => typeof v !== "string") ||
              !Number.isFinite(Date.parse(x.review.recordedUtc)) ||
              !Number.isFinite(Date.parse(x.review.validUntilUtc)),
          ) ||
          !(
            body.nextBefore === null ||
            (Number.isSafeInteger(body.nextBefore) && body.nextBefore > 0)
          )
        )
          throw new Error("Spare review response was not recognized.");
        if (!controller.signal.aborted) {
          setHistory(body);
          setError("");
        }
      } catch (e) {
        if (!controller.signal.aborted)
          setError(
            e instanceof Error
              ? e.message
              : "Spare reviews could not be refreshed.",
          );
      } finally {
        pending = false;
      }
    }
    void load();
    const timer = setInterval(() => void load(), 30000);
    return () => {
      controller.abort();
      clearInterval(timer);
    };
  }, [diskId, open, before, retry]);
  const when = (value: string) =>
    new Date(value).toLocaleString(undefined, { timeZone: timezone });
  return (
    <div className="spare-review">
      <Button
        variant="ghost"
        size="sm"
        onClick={() => {
          setChoices(spares);
          setRequestId(crypto.randomUUID());
          setMessage("");
          setOpen(true);
        }}
      >
        Check a spare
      </Button>
      {message && !open && <p role="status">{message}</p>}
      <Dialog
        open={open}
        onOpenChange={(value) => {
          if (!busy) setOpen(value);
        }}
      >
        <DialogContent className="planning-dialog">
          <DialogHeader>
            <DialogTitle>Review spare compatibility</DialogTitle>
            <DialogDescription>
              Save your evidence for this drive. This only updates Homelab
              Control’s records.
            </DialogDescription>
          </DialogHeader>
          <form
            onSubmit={async (event) => {
              event.preventDefault();
              const form = new FormData(event.currentTarget);
              const spare = choices.find((x) => x.id === form.get("asset"));
              if (!spare) return;
              setBusy(true);
              setMessage("");
              try {
                const response = await fetch(
                  `/api/v1/disks/${diskId}/spare-reviews`,
                  {
                    method: "POST",
                    signal: AbortSignal.timeout(15000),
                    headers: {
                      "Content-Type": "application/json",
                      "X-Homelab-Request": "1",
                    },
                    body: JSON.stringify({
                      requestId,
                      assetId: spare.id,
                      assetUpdatedUtc: spare.updatedUtc,
                      confirmedIdentity: form.get("identity"),
                      verdict: form.get("verdict"),
                      evidence: form.get("evidence"),
                      validUntilUtc: `${form.get("until")}T00:00:00Z`,
                    }),
                  },
                );
                if (!response.ok) {
                  const body = await response.json().catch(() => ({}));
                  throw new Error(body.error || "Review could not be saved.");
                }
                setOpen(false);
                setBefore(null);
                setHistory(null);
                setRetry((x) => x + 1);
                setMessage(
                  "Compatibility review saved. No stock was reserved.",
                );
                onChanged?.();
              } catch (e) {
                setMessage(
                  `${e instanceof Error ? e.message : "Save failed."} If the response was interrupted, refresh reviews before starting another record. Retrying unchanged is safe.`,
                );
              } finally {
                setBusy(false);
              }
            }}
          >
            {message && (
              <p role="status" className="error" tabIndex={-1} ref={messageRef}>
                {message}
              </p>
            )}
            <div className="field">
              <Label htmlFor={`spare-${diskId}`}>Spare inventory item</Label>
              <SelectField id={`spare-${diskId}`} name="asset" required>
                {choices.map((s) => (
                  <SelectOption key={s.id} value={s.id}>
                    {s.name} · {s.quantity} on hand
                  </SelectOption>
                ))}
              </SelectField>
            </div>
            <div className="field">
              <Label htmlFor={`verdict-${diskId}`}>
                Compatibility assessment
              </Label>
              <SelectField
                id={`verdict-${diskId}`}
                name="verdict"
                defaultValue="unknown"
              >
                <SelectOption value="unknown">
                  Unknown — needs investigation
                </SelectOption>
                <SelectOption value="compatible">
                  Compatible — manually reviewed
                </SelectOption>
                <SelectOption value="incompatible">Incompatible</SelectOption>
              </SelectField>
            </div>
            <div className="field">
              <Label htmlFor={`evidence-${diskId}`}>
                Compatibility evidence
              </Label>
              <textarea
                id={`evidence-${diskId}`}
                name="evidence"
                required
                maxLength={4000}
                rows={4}
                aria-describedby={`help-${diskId}`}
              />
              <small id={`help-${diskId}`}>
                Record checked capacity, interface, sector format, physical fit
                and the source of those requirements. State anything unknown.
              </small>
            </div>
            <div className="field">
              <Label htmlFor={`until-${diskId}`}>
                Review again by (00:00 UTC)
              </Label>
              <Input id={`until-${diskId}`} name="until" type="date" required />
            </div>
            <div className="field">
              <Label htmlFor={`identity-${diskId}`}>
                Confirm drive serial or WWN
              </Label>
              <Input
                id={`identity-${diskId}`}
                name="identity"
                required
                maxLength={256}
                autoComplete="off"
              />
            </div>
            <Button disabled={busy}>
              {busy ? "Saving…" : "Save compatibility review"}
            </Button>
          </form>
          <section aria-label="Spare compatibility reviews">
            <p className="muted">
              Manual assessments for this drive. Check usable capacity,
              interface, sector format and physical fit against the NAS and
              enclosure requirements. Stock is shared across plans; a review
              does not reserve a unit or authorize replacement.
            </p>

            {!spares.length && (
              <p>Add a spare in Inventory before recording a review.</p>
            )}
            {message && !open && <p role="status">{message}</p>}
            {error && (
              <div role="alert">
                <p className="error">
                  {error}{" "}
                  {history &&
                    "Showing previously loaded reviews; refresh before relying on them."}
                </p>
                <Button
                  variant="outline"
                  onClick={() => setRetry((x) => x + 1)}
                >
                  Retry spare reviews
                </Button>
              </div>
            )}
            {!history && !error && <p>Loading spare reviews…</p>}
            {history && !history.items.length && (
              <p>
                No reviews recorded on this page. Inventory notes alone do not
                establish compatibility.
              </p>
            )}
            {history?.items.map(({ review, status }) => (
              <article className="planning-stock" key={review.id}>
                <div className="section-title">
                  <strong>{review.assetName}</strong>
                  <Badge variant="outline">
                    {status === "reviewed compatible" &&
                    (error || now - Date.parse(history.generatedUtc) > 60000)
                      ? "refresh needed"
                      : status === "reviewed compatible" &&
                          Date.parse(review.validUntilUtc) <= now
                        ? "review expired"
                        : status}
                  </Badge>
                </div>
                <p>
                  Recorded assessment: {review.verdict} · Review again by{" "}
                  {when(review.validUntilUtc)}
                </p>
                <div>
                  <p className="whitespace-pre-wrap">{review.evidence}</p>
                  <small>
                    {review.author} · Recorded {when(review.recordedUtc)}
                  </small>
                </div>
              </article>
            ))}
            {history && (
              <div className="review-actions">
                {before && (
                  <Button
                    variant="ghost"
                    onClick={() => {
                      setHistory(null);
                      setBefore(null);
                    }}
                  >
                    Latest spare reviews
                  </Button>
                )}
                {history.nextBefore && (
                  <Button
                    variant="ghost"
                    onClick={() => {
                      setHistory(null);
                      setBefore(history.nextBefore);
                    }}
                  >
                    Older spare reviews
                  </Button>
                )}
              </div>
            )}
            <p className="muted">
              To correct a review, record a new assessment for the same spare.
              Earlier evidence is retained and marked superseded. Inventory
              edits invalidate prior reviews.
            </p>
          </section>
        </DialogContent>
      </Dialog>
    </div>
  );
}
