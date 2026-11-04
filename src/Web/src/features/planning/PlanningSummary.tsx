import { Calendar, Clock, TriangleAlert } from "lucide-react";
import { Card } from "@/components/ui/card";
import type { Plan } from "./planning";
import { dueDays, money, priorities, priority } from "./planning";
import type { PlanningData } from "./planning";
import { PlanningSkeleton as Skeleton } from "./PlanningSkeleton";

export function PlanningSummary({
  data,
  error,
  plans,
  timezone,
  now,
}: {
  data: PlanningData | null;
  error: string;
  plans: Plan[];
  timezone: string;
  now: number;
}) {
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

  return (
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
              <strong className="planning-number">{data.reviews.length}</strong>
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
                <span className="bar-progress" style={{ flex: b.ordered }} />
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
  );
}
