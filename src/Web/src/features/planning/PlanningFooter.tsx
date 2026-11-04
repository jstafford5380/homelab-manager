import { Link } from "react-router";
import type { PlanningData } from "./planning";
import { planningTime } from "./planning";

export function PlanningFooter({
  data,
  timezone,
}: {
  data: PlanningData | null;
  timezone: string;
}) {
  const time = (value: string | number) => planningTime(value, timezone);
  return (
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
  );
}
