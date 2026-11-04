import { Link } from "react-router";
import { CalendarClock } from "lucide-react";
import { Card } from "@/components/ui/card";
import type { Finding, Snapshot } from "../../app/overviewSnapshot";
import { OverviewTime as Time } from "./OverviewTime";
import { type PlanningSummary, buildUpcoming } from "./overviewModel";

export function OverviewUpcoming({
  snapshot,
  issues,
  planning,
  now,
}: {
  snapshot: Snapshot;
  issues: Finding[];
  planning: PlanningSummary | "unavailable" | null;
  now: number;
}) {
  const { upcoming, buckets } = buildUpcoming(snapshot, issues, planning, now);
  return (
    <section aria-labelledby="ov-upcoming-title">
      <div className="section-title">
        <h2 id="ov-upcoming-title">Coming up</h2>
        <Link to="/planning" className="ov-section-link">
          Open planning
        </Link>
      </div>
      <Card className="ov-upcoming">
        {upcoming.length ? (
          buckets
            .filter((b) => b.items.length)
            .map((b) => (
              <div key={b.label} className="ov-bucket">
                <h3
                  className={`ov-bucket-label${b.label === "Overdue" ? " is-overdue" : ""}`}
                >
                  {b.label}
                </h3>
                <ul>
                  {b.items.map((item) => (
                    <li key={item.key}>
                      <item.icon
                        size={16}
                        aria-hidden="true"
                        style={
                          item.iconTone
                            ? { color: `var(--tone-${item.iconTone})` }
                            : undefined
                        }
                      />
                      <div>
                        <Link to={item.to}>{item.title}</Link>
                        <small>
                          {item.detail}
                          {item.at && (
                            <>
                              {" · "}
                              <Time value={item.at} now={now} />
                            </>
                          )}
                        </small>
                      </div>
                    </li>
                  ))}
                </ul>
              </div>
            ))
        ) : (
          <div className="ov-upcoming-empty">
            <CalendarClock size={20} aria-hidden="true" />
            <p>
              {planning === null
                ? "Checking planning reminders…"
                : "Nothing scheduled. Add warranty dates to drives and review dates to plans, and reminders will collect here."}
            </p>
          </div>
        )}
        {planning === "unavailable" && (
          <p className="muted ov-upcoming-note" role="status">
            Drive age reminders are unavailable right now.
          </p>
        )}
      </Card>
    </section>
  );
}
