import { formatInvestment } from "../../lib/investment";
import { Link } from "react-router";
import type { Snapshot } from "../../app/overviewSnapshot";
import { OverviewTime as Time } from "./OverviewTime";
import { toneIcon, plural, buildCondition } from "./overviewModel";

export function OverviewCondition({
  condition,
  investment,
  systemCount,
  now,
}: {
  condition: ReturnType<typeof buildCondition>;
  investment: Snapshot["investment"];
  systemCount: number;
  now: number;
}) {
  const { issues, verdict, signals, latestCollection } = condition;
  const VerdictIcon = toneIcon[verdict.tone];
  return (
    <section
      className={`ov-verdict tone-${verdict.tone}`}
      aria-labelledby="ov-verdict-title"
    >
      <div className="ov-verdict-main">
        <span className="ov-verdict-icon" aria-hidden="true">
          <VerdictIcon size={22} />
        </span>
        <div>
          <p className="eyebrow">CURRENT CONDITION</p>
          <h2 id="ov-verdict-title">{verdict.headline}</h2>
          <p className="ov-verdict-detail">
            {verdict.detail}
            {latestCollection && (
              <>
                {" "}
                Latest collection <Time
                  value={latestCollection}
                  now={now}
                />{" "}
                from {plural(systemCount, "system")}.
              </>
            )}
          </p>
        </div>
      </div>
      <ul className="ov-signals" aria-label="Condition summary">
        {signals.map((s) => {
          const Icon = toneIcon[s.tone];
          const className = `ov-signal tone-${s.tone}`;
          const content = (
            <>
              <span className="ov-signal-label">
                <s.icon size={14} aria-hidden="true" /> {s.label}
              </span>
              <strong>
                {s.status !== false && (
                  <Icon size={16} aria-hidden="true" className="ov-tone-icon" />
                )}
                {s.value}
              </strong>
              <small>{s.detail}</small>
            </>
          );
          return (
            <li key={s.label}>
              {s.to === "/" && (!issues.length || !systemCount) ? (
                <div className={className}>{content}</div>
              ) : (
                <Link
                  to={s.to}
                  className={className}
                  onClick={(event) => {
                    if (s.to !== "/") return;
                    event.preventDefault();
                    document
                      .getElementById("ov-attention")
                      ?.scrollIntoView({ behavior: "smooth" });
                  }}
                >
                  {content}
                </Link>
              )}
            </li>
          );
        })}
        <li>
          <div className="ov-signal">
            <span className="ov-signal-label">Total Investment</span>
            <strong>{formatInvestment(investment)}</strong>
            <small>Active equipment</small>
          </div>
        </li>
      </ul>
    </section>
  );
}
