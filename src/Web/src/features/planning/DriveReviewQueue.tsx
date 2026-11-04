import { Link } from "react-router";
import { CheckCircle2, TriangleAlert } from "lucide-react";
import { Card } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import type { Plan } from "./planning";
import { priority, type DriveReview } from "./planning";
import type { PlanningData } from "./planning";
import { planningTime } from "./planning";
import { PlanningSkeleton as Skeleton } from "./PlanningSkeleton";
import { DriveReviewRow } from "./DriveReviewRow";

export function DriveReviewQueue({
  data,
  error,
  failedAt,
  openPlans,
  now,
  timezone,
  startPlan,
  retry,
}: {
  data: PlanningData | null;
  error: string;
  failedAt: number | null;
  openPlans: Plan[];
  now: number;
  timezone: string;
  startPlan: (review: DriveReview) => void;
  retry: () => void;
}) {
  const time = (value: string | number) => planningTime(value, timezone);
  return (
    <section aria-labelledby="planning-reviews-title" id="planning-reviews">
      <div className="section-title">
        <h2 id="planning-reviews-title">
          Drives to review{" "}
          {data && (
            <span className="planning-count">{data.reviews.length}</span>
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
          <Button variant="outline" size="sm" onClick={() => retry()}>
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
            data.reviews.map((review, index) => (
              <DriveReviewRow
                key={review.diskId}
                review={review}
                id={
                  data.reviews.findIndex(
                    (r) => priority(r) === priority(review),
                  ) === index
                    ? `planning-review-${priority(review)}`
                    : undefined
                }
                openPlans={openPlans}
                now={now}
                stale={!!error}
                spares={data.spares}
                timezone={timezone}
                onStartPlan={startPlan}
                onSpareChanged={retry}
              />
            ))
          )}
        </Card>
      )}
    </section>
  );
}
