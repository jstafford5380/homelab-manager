import { useEffect, useState } from "react";
import { Link } from "react-router";
import {
  AlarmClock,
  ArrowRight,
  BellOff,
  CalendarClock,
  Check,
  CircleAlert,
  CircleCheck,
  CircleHelp,
  Database,
  HardDrive,
  History,
  Info,
  Package,
  Plus,
  ShieldCheck,
  ShoppingBag,
  TriangleAlert,
  type LucideIcon,
} from "lucide-react";
import { Button } from "@/components/ui/button";
import { Card } from "@/components/ui/card";
import { ScanOverview, type ScanReport } from "./ScanOverview";
import { capabilityLabel } from "./capabilityLabels";
import { useDisplayFormat } from "./displayPreferences";
import type { DisplayPreferences } from "./displayPreferences";
import type { Plan } from "./PlanEditor";

export type Connection = {
  id: string;
  name: string;
  status: string;
  version?: string | null;
  lastSuccessUtc: string | null;
  pollSeconds: number;
  enabled?: boolean;
};
export type Disk = {
  id: string;
  connectionId: string;
  lastSeenUtc: string;
  serial: string;
  model: string;
  alias: string;
  wwn: string | null;
  mappingConfidence: string;
  bay: string | null;
  bayVerified: boolean;
  bayEvidence: string | null;
  present: boolean;
  purchasedUtc: string | null;
  warrantyEndUtc: string | null;
  notes: string;
  lifecycle: string;
  purchaseDateConfidence: string;
};
export type Finding = {
  id: string;
  connectionId?: string;
  diskId?: string | null;
  title: string;
  evidence: string;
  nextStep: string;
  severity: string;
  state: string;
  firstSeenUtc?: string;
  lastSeenUtc?: string;
  snoozedUntilUtc: string | null;
  reviewUtc: string | null;
};
export type Recovery = {
  id: string;
  asset: string;
  destination: string;
  mechanism: string;
  rpoHours: number;
  recoveryPointUtc: string | null;
  restoreResult: string;
};
export type Snapshot = {
  scanReports?: ScanReport[];
  recoveryAssessments: {
    policyId: string;
    assessment: { protection: string };
  }[];
  capabilities: { connectionId: string; name: string; status: string }[];
  settings: DisplayPreferences;
  connections: Connection[];
  disks: Disk[];
  findings: Finding[];
  plans: Plan[];
  recovery: Recovery[];
  activity: { id: number; action: string; detail: string; atUtc: string }[];
};
type PlanningSummary = {
  driveAgeReviewYears: number;
  reviews: {
    diskId: string;
    model: string;
    serial: string;
    reasons: { kind: string }[];
  }[];
  spares: { id: string; quantity: number }[];
  budgets: {
    currency: string;
    total: number;
    withoutEstimate: number;
    count: number;
  }[];
};

type Tone = "ok" | "warn" | "bad" | "neutral";
const toneIcon: Record<Tone, LucideIcon> = {
  ok: CircleCheck,
  warn: TriangleAlert,
  bad: CircleAlert,
  neutral: CircleHelp,
};
const severityRank: Record<string, number> = {
  critical: 0,
  urgent: 1,
  review: 2,
};
const severityIcon: Record<string, LucideIcon> = {
  critical: CircleAlert,
  urgent: TriangleAlert,
  review: Info,
};
const day = 86400000;
const relative = new Intl.RelativeTimeFormat(undefined, { numeric: "auto" });

function when(value: string, now: number) {
  const delta = Date.parse(value) - now;
  const minutes = Math.round(delta / 60000);
  if (Math.abs(minutes) < 1) return "just now";
  if (Math.abs(minutes) < 60) return relative.format(minutes, "minute");
  const hours = Math.round(minutes / 60);
  if (Math.abs(hours) < 24) return relative.format(hours, "hour");
  const days = Math.round(hours / 24);
  if (Math.abs(days) < 45) return relative.format(days, "day");
  const months = Math.round(days / 30);
  if (Math.abs(months) < 18) return relative.format(months, "month");
  return relative.format(Math.round(days / 365), "year");
}
function plural(count: number, one: string, many = `${one}s`) {
  return `${count} ${count === 1 ? one : many}`;
}
function money(amount: number, currency: string) {
  return new Intl.NumberFormat(undefined, {
    style: "currency",
    currency,
    maximumFractionDigits: 0,
  }).format(amount);
}
// Mirrors the backend's freshness window: three poll intervals, never under five minutes.
function freshness(connection: Connection, now: number) {
  if (connection.enabled === false) return "paused";
  if (!connection.lastSuccessUtc) return "never";
  const window = Math.max(300, connection.pollSeconds * 3) * 1000;
  return now - Date.parse(connection.lastSuccessUtc) > window
    ? "stale"
    : "fresh";
}
function Time({ value, now }: { value: string; now: number }) {
  const { time } = useDisplayFormat();
  return (
    <time dateTime={value} title={time(value)}>
      {when(value, now)}
    </time>
  );
}

export function OverviewView({
  snapshot,
  refreshRevision,
  busy,
  onFindingAction,
  onConnect,
}: {
  snapshot: Snapshot;
  refreshRevision: number;
  busy: boolean;
  onFindingAction: (path: string, body: unknown) => void;
  onConnect: () => void;
}) {
  const [now, setNow] = useState(Date.now);
  const [planning, setPlanning] = useState<
    PlanningSummary | "unavailable" | null
  >(null);
  const [showAll, setShowAll] = useState(false);
  useEffect(() => {
    const timer = setInterval(() => setNow(Date.now()), 30000);
    return () => clearInterval(timer);
  }, []);
  useEffect(() => {
    const controller = new AbortController();
    fetch("/api/v1/planning/review", { signal: controller.signal })
      .then((response) => (response.ok ? response.json() : null))
      .then((data) =>
        setPlanning(
          data && Array.isArray(data.reviews) && Array.isArray(data.budgets)
            ? data
            : "unavailable",
        ),
      )
      .catch(() => {
        if (!controller.signal.aborted) setPlanning("unavailable");
      });
    return () => controller.abort();
  }, [refreshRevision]);

  const { connections, capabilities, disks, plans } = snapshot;
  const systemName = (id?: string) =>
    connections.find((c) => c.id === id)?.name;
  const issues = snapshot.findings
    .filter((f) => f.state !== "resolved")
    .sort(
      (a, b) =>
        (severityRank[a.severity] ?? 3) - (severityRank[b.severity] ?? 3) ||
        Number(a.state === "acknowledged") -
          Number(b.state === "acknowledged") ||
        Date.parse(b.lastSeenUtc ?? "") - Date.parse(a.lastSeenUtc ?? ""),
    );
  const counts = (severity: string) =>
    issues.filter((f) => f.severity === severity).length;
  const critical = counts("critical");
  const urgent = counts("urgent");
  const review = issues.length - critical - urgent;
  const gaps = capabilities.filter((c) => c.status !== "supported").length;
  const unchecked = connections.filter(
    (c) => !capabilities.some((x) => x.connectionId === c.id),
  ).length;
  const stale = connections.filter((c) => freshness(c, now) !== "fresh");
  const coverageIncomplete = gaps > 0 || unchecked > 0 || stale.length > 0;
  const recoveryGaps = snapshot.recoveryAssessments.filter(
    (r) => r.assessment.protection !== "scoped recovery demonstrated",
  ).length;
  const openPlans = plans.filter((p) => p.status !== "complete");
  const latestCollection = connections
    .map((c) => c.lastSuccessUtc)
    .filter((x): x is string => !!x)
    .sort()
    .at(-1);

  const verdict: { tone: Tone; headline: string; detail: string } =
    connections.length === 0
      ? {
          tone: "neutral",
          headline: "Connect your first system to begin",
          detail:
            "Nothing is being monitored yet, so no condition can be reported.",
        }
      : critical > 0
        ? {
            tone: "bad",
            headline: `${plural(critical, "critical finding")} ${critical === 1 ? "needs" : "need"} attention`,
            detail:
              "Review the evidence and your recovery readiness before taking any physical action.",
          }
        : issues.length > 0
          ? {
              tone: "warn",
              headline: `${plural(issues.length, "finding")} to review`,
              detail: "No critical findings in the collected evidence.",
            }
          : coverageIncomplete
            ? {
                tone: "neutral",
                headline: "No open findings, but monitoring is incomplete",
                detail:
                  "Some sources are missing, unsupported or out of date, so problems there would not show up.",
              }
            : {
                tone: "ok",
                headline: "No open findings in current evidence",
                detail:
                  "All monitored sources reported recently. This reflects what was collected, not a guarantee.",
              };
  const VerdictIcon = toneIcon[verdict.tone];

  const signals: {
    label: string;
    value: string;
    detail: string;
    tone: Tone;
    to: string;
    icon: LucideIcon;
    status?: false;
  }[] = [
    {
      label: "Findings",
      value: issues.length ? `${issues.length} open` : "None open",
      detail: issues.length
        ? [
            critical && `${critical} critical`,
            urgent && `${urgent} urgent`,
            review && `${review} review`,
          ]
            .filter(Boolean)
            .join(" · ")
        : connections.length
          ? "Based on collected evidence"
          : "No systems connected",
      tone: critical
        ? "bad"
        : issues.length
          ? "warn"
          : connections.length
            ? "ok"
            : "neutral",
      to: "/",
      icon: CircleAlert,
    },
    {
      label: "Coverage",
      value:
        connections.length === 0
          ? "Not connected"
          : coverageIncomplete
            ? "Incomplete"
            : "Current",
      detail:
        connections.length === 0
          ? "Connect a NAS to collect evidence"
          : coverageIncomplete
            ? [
                gaps + unchecked && plural(gaps + unchecked, "source gap"),
                stale.length && `${stale.length} out of date`,
              ]
                .filter(Boolean)
                .join(" · ")
            : "All sources reporting",
      tone:
        connections.length === 0
          ? "neutral"
          : coverageIncomplete
            ? "warn"
            : "ok",
      to: "/storage",
      icon: Database,
    },
    {
      label: "Recovery",
      value: snapshot.recovery.length
        ? recoveryGaps
          ? plural(recoveryGaps, "gap")
          : "Demonstrated"
        : "Unverified",
      detail: snapshot.recovery.length
        ? `${plural(snapshot.recovery.length, "protection policy", "protection policies")}`
        : "No restore evidence recorded",
      tone: snapshot.recovery.length
        ? recoveryGaps
          ? "warn"
          : "ok"
        : "neutral",
      to: "/recovery",
      icon: ShieldCheck,
    },
    {
      label: "Plans",
      value: openPlans.length ? `${openPlans.length} open` : "None open",
      detail:
        openPlans.length &&
        planning &&
        planning !== "unavailable" &&
        planning.budgets.length
          ? planning.budgets
              .map((b) => `${money(b.total, b.currency)} budgeted`)
              .join(" · ")
          : "Replacements, spares and reviews",
      tone: "neutral",
      to: "/planning",
      icon: ShoppingBag,
      status: false,
    },
  ];

  type Upcoming = {
    key: string;
    at: string | null;
    title: string;
    detail: string;
    to: string;
    icon: LucideIcon;
  };
  const upcoming: Upcoming[] = [
    ...openPlans
      .filter((p) => p.dueUtc)
      .map((p) => ({
        key: `plan:${p.id}`,
        at: p.dueUtc,
        title: p.title,
        detail: `Plan review${p.status === "ordered" ? " · ordered" : ""}`,
        to: "/planning",
        icon: ShoppingBag,
      })),
    ...issues
      .filter((f) => f.state === "acknowledged" && f.reviewUtc)
      .map((f) => ({
        key: `finding:${f.id}`,
        at: f.reviewUtc,
        title: f.title,
        detail: "Acknowledgement ends",
        to: `/?finding=${f.id}`,
        icon: AlarmClock,
      })),
    ...disks
      .filter(
        (d) =>
          d.lifecycle !== "retired" &&
          d.warrantyEndUtc &&
          Date.parse(d.warrantyEndUtc) > now - 90 * day &&
          Date.parse(d.warrantyEndUtc) < now + 365 * day,
      )
      .map((d) => ({
        key: `warranty:${d.id}`,
        at: d.warrantyEndUtc,
        title: `${d.model || "Drive"} · ${d.serial}`,
        detail:
          Date.parse(d.warrantyEndUtc!) < now
            ? "Recorded warranty ended"
            : "Recorded warranty ends",
        to: `/storage?disk=${d.id}`,
        icon: CalendarClock,
      })),
  ];
  if (planning && planning !== "unavailable") {
    const aged = planning.reviews.filter((r) =>
      r.reasons.some((x) => x.kind === "age-review"),
    );
    const undated = planning.reviews.filter((r) =>
      r.reasons.some((x) => x.kind === "age-unknown"),
    );
    if (aged.length)
      upcoming.push({
        key: "age-review",
        at: null,
        title: `${plural(aged.length, "drive")} past your ${planning.driveAgeReviewYears}-year review age`,
        detail: "Worth budgeting for. Age alone is not evidence of failure.",
        to: "/planning",
        icon: HardDrive,
      });
    if (undated.length)
      upcoming.push({
        key: "age-unknown",
        at: null,
        title: `${plural(undated.length, "drive")} without a purchase date`,
        detail: "Add one so age and warranty reminders can appear here.",
        to: "/planning",
        icon: HardDrive,
      });
  }
  const buckets: { label: string; items: Upcoming[] }[] = [
    { label: "Overdue", items: [] },
    { label: "Next 30 days", items: [] },
    { label: "Next 90 days", items: [] },
    { label: "Later this year", items: [] },
    { label: "Planning reminders", items: [] },
  ];
  for (const item of upcoming.sort(
    (a, b) => Date.parse(a.at ?? "") - Date.parse(b.at ?? ""),
  )) {
    const delta = item.at ? Date.parse(item.at) - now : null;
    const index =
      delta === null
        ? 4
        : delta < 0
          ? 0
          : delta <= 30 * day
            ? 1
            : delta <= 90 * day
              ? 2
              : 3;
    buckets[index].items.push(item);
  }
  const spares =
    planning && planning !== "unavailable"
      ? planning.spares.reduce((sum, s) => sum + s.quantity, 0)
      : null;
  const scanCount = snapshot.scanReports?.length ?? 0;
  const scanActive = scanCount > 0;
  const shown = showAll ? issues : issues.slice(0, 5);

  return (
    <div className="ov">
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
                  from {plural(connections.length, "system")}.
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
                    <Icon
                      size={16}
                      aria-hidden="true"
                      className="ov-tone-icon"
                    />
                  )}
                  {s.value}
                </strong>
                <small>{s.detail}</small>
              </>
            );
            return (
              <li key={s.label}>
                {s.to === "/" && (!issues.length || !connections.length) ? (
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
        </ul>
      </section>

      {connections.length === 0 ? (
        <>
          <Card className="empty">
            <ShieldCheck size={32} />
            <h3>Meet your NAS</h3>
            <p>
              Connect your NAS to see storage topology, disk identity, and
              collection coverage. Missing data will stay visibly unknown.
            </p>
            <Button onClick={onConnect}>
              <Plus size={16} /> Connect a NAS
            </Button>
          </Card>
          {/* Retained scan reports can outlive a removed connection. */}
          {scanActive && <ScanOverview reports={snapshot.scanReports} />}
        </>
      ) : (
        <div className="ov-columns">
          <div className="ov-column">
            {issues.length > 0 && (
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
                              <Check size={12} aria-hidden="true" />{" "}
                              Acknowledged
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
                              <BellOff size={12} aria-hidden="true" />{" "}
                              Deliveries snoozed until{" "}
                              <Time value={item.snoozedUntilUtc} now={now} />
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
                                onFindingAction(
                                  `/findings/${item.id}/acknowledge`,
                                  {},
                                )
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
                                      untilUtc: new Date(
                                        Date.now() + day,
                                      ).toISOString(),
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
                    {showAll
                      ? "Show fewer"
                      : `Show all ${issues.length} findings`}
                  </Button>
                )}
                <p className="muted ov-footnote">
                  Critical findings bypass delivery snoozes. Acknowledging never
                  changes alerts on your NAS.
                </p>
              </section>
            )}
            {scanActive && <ScanOverview reports={snapshot.scanReports} />}
            <OverviewSystems snapshot={snapshot} issues={issues} now={now} />
          </div>

          <div className="ov-column ov-rail">
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
                              <item.icon size={16} aria-hidden="true" />
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

            <section aria-labelledby="ov-ready-title">
              <div className="section-title">
                <h2 id="ov-ready-title">Readiness</h2>
              </div>
              <Card className="ov-ready">
                <div>
                  <Package size={18} aria-hidden="true" />
                  <div>
                    <strong>
                      {spares === null
                        ? "Spares unknown"
                        : spares
                          ? plural(spares, "spare")
                          : "No spares recorded"}
                    </strong>
                    <small>
                      {spares === null
                        ? "Planning data isn't available right now."
                        : spares === 0
                          ? "A compatible spare on the shelf shortens a rebuild."
                          : "Recorded in inventory"}
                    </small>
                  </div>
                </div>
                <div>
                  <ShoppingBag size={18} aria-hidden="true" />
                  <div>
                    <strong>
                      {planning &&
                      planning !== "unavailable" &&
                      planning.budgets.length
                        ? planning.budgets
                            .map((b) => money(b.total, b.currency))
                            .join(" + ")
                        : openPlans.length
                          ? "Budget not set"
                          : "No open plans"}
                    </strong>
                    <small>
                      {planning &&
                      planning !== "unavailable" &&
                      planning.budgets.some((b) => b.withoutEstimate)
                        ? `${plural(
                            planning.budgets.reduce(
                              (s, b) => s + b.withoutEstimate,
                              0,
                            ),
                            "plan",
                          )} without an estimate`
                        : `Across ${plural(openPlans.length, "open plan")}`}
                    </small>
                  </div>
                </div>
                <div>
                  <History size={18} aria-hidden="true" />
                  <div>
                    <strong>
                      {scanActive
                        ? `${plural(scanCount, "scan report")} in progress`
                        : snapshot.scanReports
                          ? "No scrub or resilver in progress"
                          : "Scan summary unavailable"}
                    </strong>
                    <small>
                      {scanActive ? (
                        "Shown under Scrub & resilver reports."
                      ) : (
                        <>
                          {snapshot.scanReports
                            ? "In the latest retained pool snapshots. This does not establish that all pools were checked. "
                            : "Review storage history and collection coverage. "}
                          <Link to="/storage">Review storage history</Link>
                        </>
                      )}
                    </small>
                  </div>
                </div>
              </Card>
            </section>
          </div>
        </div>
      )}
    </div>
  );
}

const sourceLabels: Record<string, string> = {
  system_identity: "System identity",
  inventory: "Drive inventory",
  pools: "Pools",
  datasets: "Datasets",
  alerts: "NAS alerts",
  cached_temperatures: "Drive temperatures",
  smart: "SMART health",
  recovery: "Restore evidence",
};

const sourceStatuses: Record<string, [Tone, string]> = {
  supported: ["ok", "Reporting"],
  disabled: ["neutral", "Turned off"],
  unsupported: ["neutral", "Unsupported"],
  not_reported: ["neutral", "Not reported"],
  missing: ["neutral", "Not reported"],
  unavailable: ["warn", "Unavailable"],
  denied: ["warn", "Access denied"],
  stale: ["warn", "Out of date"],
};

const systemStatuses = {
  fresh: ["ok", "Reporting"],
  paused: ["neutral", "Paused"],
  never: ["warn", "No collection yet"],
  stale: ["warn", "Out of date"],
} as const;

function StatusPill({ tone, label }: { tone: Tone; label: string }) {
  const Icon = toneIcon[tone];
  return (
    <span className={`ov-pill tone-${tone}`}>
      <Icon size={12} aria-hidden="true" /> {label}
    </span>
  );
}

function OverviewSystems({
  snapshot,
  issues,
  now,
}: {
  snapshot: Snapshot;
  issues: Finding[];
  now: number;
}) {
  const { connections, capabilities, disks } = snapshot;
  const systems = connections.map((connection) => {
    const sources = capabilities.filter(
      (source) => source.connectionId === connection.id,
    );
    const gaps = sources.filter((source) => source.status !== "supported");
    const [tone, label] = systemStatuses[freshness(connection, now)];
    const stats = [
      {
        label: "Last collected",
        value: connection.lastSuccessUtc ? (
          <Time value={connection.lastSuccessUtc} now={now} />
        ) : (
          "Not yet observed"
        ),
      },
      {
        label: "Drives",
        value: disks.filter(
          (disk) =>
            disk.connectionId === connection.id &&
            disk.present &&
            disk.lifecycle !== "retired",
        ).length,
      },
      {
        label: "Open findings",
        value: issues.filter((issue) => issue.connectionId === connection.id)
          .length,
      },
      { label: "Source gaps", value: gaps.length },
    ];
    return {
      connection,
      name: (
        <Link to={`/settings/connections/${connection.id}`}>
          {connection.name}
        </Link>
      ),
      status: <StatusPill tone={tone} label={label} />,
      stats,
      sources: [
        ...gaps,
        ...sources.filter((source) => source.status === "supported"),
      ],
    };
  });
  const [first] = systems;

  return (
    <section aria-labelledby="ov-systems-title">
      <div className="section-title">
        <h2 id="ov-systems-title">Systems</h2>
        <span className="muted">{systems.length} enrolled</span>
      </div>
      {systems.length === 1 ? (
        <Card className="ov-system">
          <div className="ov-system-head">
            <Database size={18} aria-hidden="true" />
            <h3>{first.name}</h3>
            {first.connection.version && (
              <small>{first.connection.version}</small>
            )}
            {first.status}
          </div>
          <p className="muted ov-system-status">{first.connection.status}</p>
          <dl>
            {first.stats.map(({ label, value }) => (
              <div key={label}>
                <dt>{label}</dt>
                <dd>{value}</dd>
              </div>
            ))}
          </dl>
          <div className="ov-sources">
            <div className="ov-sources-head">
              <h4>Sources</h4>
              <Link className="ov-section-link" to="/storage">
                Review coverage
              </Link>
            </div>
            {first.sources.length ? (
              <ul aria-label={`Sources for ${first.connection.name}`}>
                {first.sources.map(({ name, status }) => {
                  const [tone, label] = sourceStatuses[status] ?? [
                    "neutral",
                    status.replaceAll("_", " "),
                  ];
                  return (
                    <li key={name}>
                      <span>{sourceLabels[name] ?? capabilityLabel(name)}</span>
                      <StatusPill tone={tone} label={label} />
                    </li>
                  );
                })}
              </ul>
            ) : (
              <p className="muted">No sources reported yet.</p>
            )}
          </div>
        </Card>
      ) : (
        <Card className="ov-system-table" role="table" aria-label="Systems">
          <div className="ov-system-row ov-system-headers" role="row">
            {["System", "Status", ...first.stats.map(({ label }) => label)].map(
              (label) => (
                <span key={label} role="columnheader">
                  {label}
                </span>
              ),
            )}
          </div>
          {systems.map(({ connection, name, status, stats }) => (
            <div key={connection.id} className="ov-system-row" role="row">
              <div className="ov-system-name" role="cell">
                <Database size={16} aria-hidden="true" />
                <div>
                  <strong>{name}</strong>
                  <small title={connection.status}>{connection.status}</small>
                </div>
              </div>
              <div className="ov-system-row-status" role="cell">
                {status}
              </div>
              {stats.map(({ label, value }) => (
                <div key={label} role="cell">
                  <span className="ov-system-cell-label" aria-hidden="true">
                    {label}
                  </span>
                  {value}
                </div>
              ))}
            </div>
          ))}
        </Card>
      )}
    </section>
  );
}
