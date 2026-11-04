import { useState } from "react";
import { Link } from "react-router";
import { ArrowRight, BellOff, Check, Info } from "lucide-react";
import { Button } from "@/components/ui/button";
import type { Connection, Finding } from "../../app/overviewSnapshot";
import { OverviewTime as Time } from "./OverviewTime";
import { severityIcon, severityRank, day } from "./overviewModel";

export function OverviewFindings({
  issues,
  connections,
  now,
  busy,
  onFindingAction,
}: {
  issues: Finding[];
  connections: Connection[];
  now: number;
  busy: boolean;
  onFindingAction: (path: string, body: unknown) => void;
}) {
  const [showAll, setShowAll] = useState(false);
  const shown = showAll ? issues : issues.slice(0, 5);
  const systemName = (id?: string) =>
    connections.find((c) => c.id === id)?.name;
  return (
    <section id="ov-attention" aria-labelledby="ov-attention-title">
      <div className="section-title">
        <h2 id="ov-attention-title">Needs your attention</h2>
        <span className="muted">Most severe first</span>
      </div>
      <ul className="ov-findings">
        {shown.map((item) => {
          const SeverityIcon = severityIcon[item.severity] ?? Info;
          const acknowledged = item.state === "acknowledged";
          return (
            <li
              key={item.id}
              className={`ov-finding sev-${severityRank[item.severity] !== undefined ? item.severity : "review"}${acknowledged ? " is-acknowledged" : ""}`}
            >
              <div className="ov-finding-meta">
                <span className="ov-severity">
                  <SeverityIcon size={13} aria-hidden="true" />
                  {item.severity}
                </span>
                {acknowledged && (
                  <span className="ov-chip">
                    <Check size={12} aria-hidden="true" /> Acknowledged
                    {item.reviewUtc && (
                      <>
                        {" · review "}
                        <Time value={item.reviewUtc} now={now} />
                      </>
                    )}
                  </span>
                )}
                {item.snoozedUntilUtc && (
                  <span className="ov-chip">
                    <BellOff size={12} aria-hidden="true" /> Deliveries snoozed
                    until <Time value={item.snoozedUntilUtc} now={now} />
                  </span>
                )}
                <span className="muted ov-finding-source">
                  {[
                    systemName(item.connectionId),
                    item.firstSeenUtc && "first seen",
                  ]
                    .filter(Boolean)
                    .join(" · ")}{" "}
                  {item.firstSeenUtc && (
                    <Time value={item.firstSeenUtc} now={now} />
                  )}
                </span>
              </div>
              <h3>
                <Link to={`/?finding=${item.id}`}>{item.title}</Link>
              </h3>
              <p className="ov-evidence">{item.evidence}</p>
              <p className="ov-next">
                <ArrowRight size={14} aria-hidden="true" />
                <span>{item.nextStep}</span>
              </p>
              <div className="ov-finding-actions">
                {!acknowledged && (
                  <Button
                    variant="outline"
                    size="sm"
                    disabled={busy}
                    onClick={() =>
                      onFindingAction(`/findings/${item.id}/acknowledge`, {})
                    }
                  >
                    <Check size={14} /> Acknowledge for 7 days
                  </Button>
                )}
                <Button
                  variant="ghost"
                  size="sm"
                  disabled={busy}
                  onClick={() =>
                    onFindingAction(
                      `/findings/${item.id}/${item.snoozedUntilUtc ? "unsnooze" : "snooze"}`,
                      item.snoozedUntilUtc
                        ? {}
                        : {
                            untilUtc: new Date(Date.now() + day).toISOString(),
                          },
                    )
                  }
                >
                  {item.snoozedUntilUtc ? (
                    "Resume deliveries"
                  ) : (
                    <>
                      <BellOff size={14} /> Snooze deliveries 24h
                    </>
                  )}
                </Button>
              </div>
            </li>
          );
        })}
      </ul>
      {issues.length > 5 && (
        <Button
          variant="outline"
          className="ov-show-all"
          aria-expanded={showAll}
          onClick={() => setShowAll(!showAll)}
        >
          {showAll ? "Show fewer" : `Show all ${issues.length} findings`}
        </Button>
      )}
      <p className="muted ov-footnote">
        Critical findings bypass delivery snoozes. Acknowledging never changes
        alerts on your NAS.
      </p>
    </section>
  );
}
