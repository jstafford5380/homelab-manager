import { ReasonChips } from "./ReasonChips";
import { Link } from "react-router";
import { useEffect, useState } from "react";
import {
  Calendar,
  CheckCircle2,
  CircleHelp,
  Clock,
  Package,
  Plus,
  TriangleAlert,
  Truck,
} from "lucide-react";
import { Card } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import {
  Tooltip,
  TooltipContent,
  TooltipProvider,
  TooltipTrigger,
} from "@/components/ui/tooltip";
import SpareReview, { type Spare } from "./SpareReview";
import { type Plan } from "./PlanEditor";
import { PlansBoard } from "./PlansBoard";
import { CreatePlanDialog } from "./CreatePlanDialog";
import {
  usePlanningClock,
  dueDays,
  focusPlan,
  money,
  priorities,
  priority,
  shortDate,
  type DriveReview,
} from "./planning";
import "./Planning.css";

type Stock = Spare & {
  estimatedUnitCost: number | null;
  currency: string;
  compatibleDriveCount: number;
};
type Data = {
  generatedUtc: string;
  ruleVersion: string;
  trackedDriveCount: number;
  driveAgeReviewYears: number;
  warrantyNoticeDays: number;
  reviews: DriveReview[];
  spares: Stock[];
};
function Skeleton({ rows = 1 }: { rows?: number }) {
  return (
    <div
      role="group"
      aria-busy="true"
      aria-label="Loading drive reviews"
      className="planning-skeletons"
    >
      {Array.from({ length: rows }, (_, i) => (
        <div className="planning-skeleton" key={i}>
          <span />
          <span />
        </div>
      ))}
    </div>
  );
}
export function PlanningReview({
  timezone,
  onChanged,
  plans,
  disks,
  createOpen,
  onCreateOpenChange,
  initialTitle,
  initialNotes,
}: {
  timezone: string;
  onChanged: (saved?: Plan) => Promise<void>;
  plans: Plan[];
  disks: { id: string; serial: string }[];
  createOpen: boolean;
  onCreateOpenChange: (value: boolean) => void;
  initialTitle?: string;
  initialNotes?: string;
}) {
  const [data, setData] = useState<Data | null>(null),
    [error, setError] = useState(""),
    [failedAt, setFailedAt] = useState<number | null>(null),
    [retry, setRetry] = useState(0),
    [selected, setSelected] = useState<DriveReview | null>(null);
  const now = usePlanningClock();
  const revision = JSON.stringify(plans);
  useEffect(() => {
    let disposed = false;
    const controller = new AbortController();
    let timer: ReturnType<typeof setTimeout>;
    async function load() {
      try {
        const response = await fetch("/api/v1/planning/review", {
          signal: AbortSignal.any([
            controller.signal,
            AbortSignal.timeout(15000),
          ]),
        });
        if (!response.ok)
          throw new Error(
            `Drive reviews couldn’t be loaded. The server returned ${response.status}.`,
          );
        const next: Data = await response.json();
        if (
          !next ||
          !Array.isArray(next.reviews) ||
          !Array.isArray(next.spares) ||
          !Number.isFinite(Date.parse(next.generatedUtc)) ||
          next.reviews.some(
            (r) =>
              !r.diskId ||
              !Array.isArray(r.reasons) ||
              !r.spareFit ||
              !Object.hasOwn(priorities, priority(r)),
          )
        )
          throw new Error("Drive reviews returned an invalid response.");
        if (!disposed) {
          setData(next);
          setError("");
          setFailedAt(null);
        }
      } catch (e) {
        if (!disposed) {
          setError(
            e instanceof Error
              ? e.message
              : "Drive reviews couldn’t be loaded.",
          );
          setFailedAt(Date.now());
        }
      } finally {
        if (!disposed) timer = setTimeout(() => void load(), 30000);
      }
    }
    void load();
    return () => {
      disposed = true;
      controller.abort();
      clearTimeout(timer);
    };
  }, [revision, retry]);
  const openPlans = plans.filter((p) => p.status !== "complete");
  const overdue = openPlans.filter(
    (p) => p.dueUtc && dueDays(p.dueUtc, timezone, now) < 0,
  ).length;
  const soon = openPlans.filter(
    (p) =>
      p.dueUtc &&
      dueDays(p.dueUtc, timezone, now) >= 0 &&
      dueDays(p.dueUtc, timezone, now) <= 14,
  ).length;
  // Spend follows the current app snapshot, even when the separate review refresh fails.
  const budgets = Array.from(new Set(openPlans.map((p) => p.currency))).map(
    (currency) => {
      const items = openPlans.filter((p) => p.currency === currency);
      return {
        currency,
        planned: items
          .filter((p) => p.status === "planned")
          .reduce((n, p) => n + (p.budget ?? 0), 0),
        ordered: items
          .filter((p) => p.status === "ordered")
          .reduce((n, p) => n + (p.budget ?? 0), 0),
        withoutEstimate: items.filter((p) => p.budget === null).length,
      };
    },
  );
  const counts = Object.keys(priorities).map((key) => ({
    key,
    ...priorities[key as keyof typeof priorities],
    count: data?.reviews.filter((r) => priority(r) === key).length ?? 0,
  }));
  const empty =
    !!data &&
    data.trackedDriveCount === 0 &&
    !plans.length &&
    !data.spares.length;
  const time = (value: string | number) =>
    new Date(value).toLocaleTimeString(undefined, {
      timeZone: timezone,
      hour: "numeric",
      minute: "2-digit",
    });
  const startPlan = (review: DriveReview | null) => {
    setSelected(review);
    onCreateOpenChange(true);
  };
  const selectedFit = selected?.spareFit;
  const fitPrice =
    selectedFit?.status === "compatible" &&
    selectedFit.validUntilUtc &&
    Date.parse(selectedFit.validUntilUtc) > now &&
    !error
      ? (data?.spares.find(
          (s) => s.id === selectedFit.assetId && s.quantity > 0,
        ) ?? null)
      : null;
  return (
    <div className="planning-page">
      {empty ? (
        <div className="planning-onboarding">
          {[
            [
              "Connect a NAS",
              "Drive reviews appear after the first inventory. They flag evidence, warranty dates and drive age.",
              "/settings/connections",
              "Connect a NAS",
            ],
            [
              "Record your spares",
              "Add spare drives in Inventory so you can check which drives they fit.",
              "/inventory",
              "Open Inventory",
            ],
            [
              "Add a plan",
              "Track a purchase, replacement or maintenance task with a budget and a date.",
              null,
              "Add plan",
            ],
          ].map(([title, copy, to, action]) => (
            <Card key={title} className="planning-onboard">
              <Package className="tone-info planning-tile" />
              <h2>{title}</h2>
              <p>{copy}</p>
              {to ? (
                <Button asChild variant="outline">
                  <Link to={to}>{action}</Link>
                </Button>
              ) : (
                <Button variant="outline" onClick={() => startPlan(null)}>
                  <Plus size={14} />
                  {action}
                </Button>
              )}
            </Card>
          ))}
        </div>
      ) : (
        <>
          <Card className="planning-summary" aria-label="Planning summary">
            <div>
              <span className="planning-summary-label">
                Drives to review{" "}
                {error && data && (
                  <span className="planning-chip tone-warn">
                    <Clock size={13} />
                    Out of date
                  </span>
                )}
              </span>
              {data ? (
                <>
                  <div>
                    <strong className="planning-number">
                      {data.reviews.length}
                    </strong>
                    <span className="muted">
                      {" "}
                      of {data.trackedDriveCount} tracked
                    </span>
                  </div>
                  <div
                    role="img"
                    aria-label={counts
                      .map((c) => `${c.count} ${c.label.toLowerCase()}`)
                      .join(", ")}
                    className="planning-bar"
                  >
                    {counts.map((c) => (
                      <span
                        key={c.key}
                        className={`bar-${c.tone}`}
                        style={{ flex: c.count }}
                      />
                    ))}
                  </div>
                  <div className="planning-legend">
                    {data.reviews.length
                      ? counts.map((c) => (
                          <a
                            key={c.key}
                            href={
                              c.count
                                ? `#planning-review-${c.key}`
                                : "#planning-reviews"
                            }
                          >
                            <i className={`bar-${c.tone}`} />
                            {c.count} {c.label.toLowerCase()}
                          </a>
                        ))
                      : "No reminders from the current rules"}
                  </div>
                </>
              ) : error ? (
                <>
                  <strong className="planning-number">Unknown</strong>
                  <span className="planning-chip tone-bad">
                    <TriangleAlert size={13} />
                    Reviews unavailable
                  </span>
                </>
              ) : (
                <Skeleton />
              )}
            </div>
            <div>
              <span className="planning-summary-label">Open plans</span>
              <strong className="planning-number">{openPlans.length}</strong>
              <div className="planning-legend">
                {!!overdue && (
                  <a href="#planning-plans" className="planning-chip tone-bad">
                    <Clock size={13} />
                    {overdue} overdue
                  </a>
                )}
                {!!soon && (
                  <a href="#planning-plans" className="planning-chip tone-warn">
                    <Calendar size={13} />
                    {soon} due in 2 weeks
                  </a>
                )}
                {!overdue && !soon && (
                  <span className="muted">Nothing due in the next 2 weeks</span>
                )}
              </div>
            </div>
            <div>
              <span className="planning-summary-label">Estimated spend</span>
              {!data && !error ? (
                <Skeleton />
              ) : budgets.length ? (
                budgets.map((b) => (
                  <div className="planning-budget" key={b.currency}>
                    <strong className="planning-number">
                      {money(b.planned + b.ordered, b.currency)}
                    </strong>
                    <div
                      role="img"
                      aria-label={`${money(b.ordered, b.currency)} ordered, ${money(b.planned, b.currency)} planned`}
                      className="planning-bar"
                    >
                      <span
                        className="bar-progress"
                        style={{ flex: b.ordered }}
                      />
                      <span className="bar-info" style={{ flex: b.planned }} />
                    </div>
                    <div className="planning-legend">
                      <a href="#planning-plans">
                        <i className="bar-progress" />
                        {money(b.ordered, b.currency)} ordered
                      </a>
                      <a href="#planning-plans">
                        <i className="bar-info" />
                        {money(b.planned, b.currency)} planned
                      </a>
                      {!!b.withoutEstimate && (
                        <a href="#planning-plans">
                          <i className="bar-warn" />
                          {b.withoutEstimate} without an estimate
                        </a>
                      )}
                    </div>
                  </div>
                ))
              ) : (
                <span className="muted">No open plans</span>
              )}
            </div>
          </Card>
          <div className="planning-review-layout">
            <section
              aria-labelledby="planning-reviews-title"
              id="planning-reviews"
            >
              <div className="section-title">
                <h2 id="planning-reviews-title">
                  Drives to review{" "}
                  {data && (
                    <span className="planning-count">
                      {data.reviews.length}
                    </span>
                  )}
                </h2>
                <small className="muted">Sorted by evidence, then serial</small>
              </div>
              {error && (
                <div
                  className={`planning-banner tone-${data ? "warn" : "bad"}`}
                  role={data ? "status" : "alert"}
                >
                  <TriangleAlert size={16} />
                  <p>
                    {data
                      ? `Showing reviews from ${time(data.generatedUtc)}. Refresh failed at ${time(failedAt!)}. Plans you add now still save.`
                      : `${error} Your plans below are current.`}
                  </p>
                  <Button
                    variant="outline"
                    size="sm"
                    onClick={() => setRetry((x) => x + 1)}
                  >
                    Retry
                  </Button>
                </div>
              )}
              {!data && !error && (
                <Card className="planning-queue">
                  <Skeleton rows={3} />
                </Card>
              )}
              {data && (
                <Card className="planning-queue">
                  {!data.reviews.length ? (
                    <div className="planning-calm">
                      <CheckCircle2 className="planning-tile tone-info" />
                      <div>
                        <h3>No drives match the planning rules.</h3>
                        <p>
                          This isn’t a health check. Check coverage in{" "}
                          <Link to="/storage">Storage</Link>.
                        </p>
                      </div>
                    </div>
                  ) : (
                    data.reviews.map((review, index) => {
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
                      const fitStatus = error
                        ? "stale"
                        : fit.status === "compatible" &&
                            (!fit.validUntilUtc ||
                              Date.parse(fit.validUntilUtc) <= now)
                          ? "expired"
                          : fit.status;
                      const fitText = error
                        ? "Spare check out of date"
                        : !data.spares.length
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
                          id={
                            data.reviews.findIndex(
                              (r) => priority(r) === key,
                            ) === index
                              ? `planning-review-${key}`
                              : undefined
                          }
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
                                <span
                                  className={`planning-priority text-${meta.tone}`}
                                >
                                  {meta.label}
                                </span>
                              )}
                              <h3>
                                {review.label ||
                                  review.model ||
                                  "Unidentified model"}
                              </h3>
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
                              {fitStatus !== "compatible" &&
                                data.spares.some((s) => s.quantity > 0) && (
                                  <SpareReview
                                    diskId={review.diskId}
                                    spares={data.spares.filter(
                                      (s) => s.quantity > 0,
                                    )}
                                    timezone={timezone}
                                    onChanged={() => setRetry((x) => x + 1)}
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
                                  In plan ·{" "}
                                  {plan.status === "ordered"
                                    ? "Ordered"
                                    : "Planned"}
                                </a>
                              ) : (
                                <Button
                                  variant={
                                    key === "media" ? "default" : "outline"
                                  }
                                  size="sm"
                                  onClick={() => startPlan(review)}
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
                                      className={`planning-chip tone-${fitStatus === "compatible" ? "ok" : fitStatus === "incompatible" ? "bad" : fitStatus === "expired" || error ? "warn" : "neutral"} ${fitStatus === "unchecked" ? "dashed" : ""}`}
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
                    })
                  )}
                </Card>
              )}
            </section>
            <section className="planning-spares" aria-label="Spares">
              <h2>
                <Package size={18} />
                Spares
              </h2>
              {!data ? (
                error ? (
                  <p>Unavailable with drive reviews.</p>
                ) : (
                  <Skeleton rows={2} />
                )
              ) : (
                <>
                  <div className="planning-stock-grid">
                    {data.spares.length ? (
                      data.spares.map((s) => (
                        <article key={s.id} className="planning-spare">
                          <span
                            className={`planning-quantity tone-${s.quantity > 0 ? "ok" : "warn"}`}
                            aria-label={`${s.quantity} on hand`}
                          >
                            {s.quantity}
                          </span>
                          <div>
                            <h3>{s.name}</h3>
                            <p>
                              <span
                                className={
                                  s.compatibleDriveCount && !error
                                    ? "text-ok"
                                    : ""
                                }
                              >
                                {error
                                  ? "Checks out of date"
                                  : s.compatibleDriveCount
                                    ? `Checked for ${s.compatibleDriveCount} drive${s.compatibleDriveCount === 1 ? "" : "s"}`
                                    : "Not checked against any drive"}
                              </span>
                              {s.estimatedUnitCost !== null &&
                                ` · ${money(s.estimatedUnitCost, s.currency)} each`}
                            </p>
                          </div>
                        </article>
                      ))
                    ) : (
                      <p>No spares recorded.</p>
                    )}
                  </div>
                  <Link to="/inventory">Manage in Inventory →</Link>
                </>
              )}
            </section>
          </div>
          <PlansBoard
            plans={plans}
            disks={disks}
            timezone={timezone}
            now={now}
            onChanged={onChanged}
            onAdd={() => startPlan(null)}
          />
        </>
      )}
      <div className="planning-rule-footer">
        <span>
          <strong>Advice only.</strong> Reminders come from your records and
          observed evidence. They don’t predict failures.
        </span>
        <span>
          {data ? (
            <>
              Review after <strong>{data.driveAgeReviewYears} years</strong> ·
              warranty notice <strong>{data.warrantyNoticeDays} days</strong>{" "}
              ·{" "}
            </>
          ) : (
            "Thresholds unavailable · "
          )}
          <Link to="/settings/review-levels">Change in Settings</Link>
        </span>
        {data && (
          <span>
            {data.ruleVersion} · generated {time(data.generatedUtc)}
          </span>
        )}
      </div>
      <CreatePlanDialog
        key={`${createOpen}-${selected?.diskId ?? "generic"}`}
        open={createOpen}
        onOpenChange={(value) => {
          onCreateOpenChange(value);
          if (!value) setSelected(null);
        }}
        review={selected}
        price={fitPrice}
        timezone={timezone}
        onChanged={onChanged}
        initialTitle={initialTitle}
        initialNotes={initialNotes}
      />
    </div>
  );
}
