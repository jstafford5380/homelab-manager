import { ReasonChips } from "./ReasonChips";
import { Calendar, CheckCircle2, CircleHelp, Truck } from "lucide-react";
import { Button } from "@/components/ui/button";
import {
  Tooltip,
  TooltipContent,
  TooltipProvider,
  TooltipTrigger,
} from "@/components/ui/tooltip";
import SpareReview from "./SpareReview";
import type { Plan } from "./planning";
import {
  focusPlan,
  priorities,
  priority,
  shortDate,
  type DriveReview,
} from "./planning";
import type { Stock } from "./planning";

export function DriveReviewRow({
  review,
  id,
  openPlans,
  now,
  stale,
  spares,
  timezone,
  onStartPlan,
  onSpareChanged,
}: {
  review: DriveReview;
  id?: string;
  openPlans: Plan[];
  now: number;
  stale: boolean;
  spares: Stock[];
  timezone: string;
  onStartPlan: (review: DriveReview) => void;
  onSpareChanged: () => void;
}) {
  const key = priority(review) as keyof typeof priorities;
  const meta = priorities[key];
  const { Icon } = meta;
  const plan = openPlans
    .filter((p) => p.diskId === review.diskId)
    .sort(
      (a, b) =>
        (a.dueUtc ? Date.parse(a.dueUtc) : Infinity) -
          (b.dueUtc ? Date.parse(b.dueUtc) : Infinity) ||
        a.id.localeCompare(b.id),
    )[0];
  const fit = review.spareFit;
  const fitStatus = stale
    ? "stale"
    : fit.status === "compatible" &&
        (!fit.validUntilUtc || Date.parse(fit.validUntilUtc) <= now)
      ? "expired"
      : fit.status;
  const fitText = stale
    ? "Spare check out of date"
    : !spares.length
      ? "No spares recorded"
      : fitStatus === "compatible"
        ? `Spare fits · ${fit.assetName}`
        : fitStatus === "expired"
          ? "Spare check expired"
          : fitStatus === "incompatible"
            ? "Spare doesn’t fit"
            : "Spare not checked";
  return (
    <article
      key={review.diskId}
      id={id}
      aria-label={`${review.label || review.model} ${review.serial}, ${meta.label}`}
      className={`planning-drive ${key === "prepare" ? "compact" : ""} ${key === "media" ? "media" : ""}`}
    >
      <Icon
        className={`planning-tile tone-${meta.tone}`}
        aria-label={meta.label}
      />
      <div className="planning-drive-main">
        <div className="planning-drive-heading">
          {key !== "prepare" && (
            <span className={`planning-priority text-${meta.tone}`}>
              {meta.label}
            </span>
          )}
          <h3>{review.label || review.model || "Unidentified model"}</h3>
          <span className="planning-serial">
            {review.serial || "Serial unknown"}
          </span>
          {key !== "prepare" && review.location && (
            <small>{review.location}</small>
          )}
        </div>
        <ReasonChips
          reasons={review.reasons}
          diskId={review.diskId}
          timezone={timezone}
          tone={meta.tone}
        />
        {key !== "prepare" && (
          <p className="planning-next">
            <strong>Next:</strong>{" "}
            {key === "media"
              ? "Check recovery readiness, then line up a compatible replacement."
              : "Review the evidence and the drive’s path (cable, port, backplane) before deciding."}
          </p>
        )}
      </div>
      <div className="planning-drive-actions">
        <div className="planning-row-buttons">
          {fitStatus !== "compatible" && spares.some((s) => s.quantity > 0) && (
            <SpareReview
              diskId={review.diskId}
              spares={spares.filter((s) => s.quantity > 0)}
              timezone={timezone}
              onChanged={onSpareChanged}
            />
          )}
          {plan ? (
            <a
              className={`planning-chip tone-${plan.status === "ordered" ? "progress" : "info"}`}
              href={`#plan-${plan.id}`}
              onClick={(e) => {
                e.preventDefault();
                focusPlan(plan.id);
              }}
            >
              {plan.status === "ordered" ? (
                <Truck size={13} />
              ) : (
                <Calendar size={13} />
              )}
              In plan · {plan.status === "ordered" ? "Ordered" : "Planned"}
            </a>
          ) : (
            <Button
              variant={key === "media" ? "default" : "outline"}
              size="sm"
              onClick={() => onStartPlan(review)}
            >
              {meta.action}
            </Button>
          )}
        </div>
        {
          <TooltipProvider>
            <Tooltip>
              <TooltipTrigger asChild>
                <span
                  tabIndex={0}
                  className={`planning-chip tone-${fitStatus === "compatible" ? "ok" : fitStatus === "incompatible" ? "bad" : fitStatus === "expired" || stale ? "warn" : "neutral"} ${fitStatus === "unchecked" ? "dashed" : ""}`}
                >
                  {fitStatus === "compatible" ? (
                    <CheckCircle2 size={13} />
                  ) : (
                    <CircleHelp size={13} />
                  )}{" "}
                  {fitText}
                </span>
              </TooltipTrigger>
              <TooltipContent className="max-w-xs">
                {fitStatus === "compatible"
                  ? `Your compatibility check. Review again by ${shortDate(fit.validUntilUtc!, timezone)}. A check doesn’t reserve a unit.`
                  : "Your recorded compatibility assessment. Inventory or identity changes require a new check."}
              </TooltipContent>
            </Tooltip>
          </TooltipProvider>
        }
      </div>
    </article>
  );
}
