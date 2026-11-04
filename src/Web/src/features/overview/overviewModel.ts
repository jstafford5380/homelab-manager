import {
  AlarmClock,
  CalendarClock,
  CircleAlert,
  CircleCheck,
  CircleHelp,
  Database,
  HardDrive,
  Info,
  ShieldCheck,
  ShoppingBag,
  TriangleAlert,
  type LucideIcon,
} from "lucide-react";
import type { Connection, Finding, Snapshot } from "../../app/overviewSnapshot";

export type PlanningSummary = {
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

/** Validate exactly the planning fields consumed by the dashboard. */
export function isPlanningSummary(value:unknown): value is PlanningSummary {
 if(!value || typeof value!=="object") return false;
 const data=value as Partial<PlanningSummary>;
 return Number.isFinite(data.driveAgeReviewYears) &&
  Array.isArray(data.reviews) && data.reviews.every(r=>r && Array.isArray(r.reasons) && r.reasons.every(reason=>reason && typeof reason.kind==="string")) &&
  Array.isArray(data.spares) && data.spares.every(s=>s && Number.isSafeInteger(s.quantity) && s.quantity>=0) &&
  Array.isArray(data.budgets) && data.budgets.every(b=>b && typeof b.currency==="string" && /^[A-Z]{3}$/.test(b.currency) && Number.isFinite(b.total) && b.total>=0 && Number.isSafeInteger(b.withoutEstimate) && b.withoutEstimate>=0);
}

export type Tone = "ok" | "warn" | "bad" | "neutral";
export const toneIcon: Record<Tone, LucideIcon> = {
  ok: CircleCheck,
  warn: TriangleAlert,
  bad: CircleAlert,
  neutral: CircleHelp,
};
export const severityRank: Record<string, number> = {
  critical: 0,
  urgent: 1,
  review: 2,
};
export const severityIcon: Record<string, LucideIcon> = {
  critical: CircleAlert,
  urgent: TriangleAlert,
  review: Info,
};
export const day = 86400000;
const relative = new Intl.RelativeTimeFormat(undefined, { numeric: "auto" });

export function when(value: string, now: number) {
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
export function plural(count: number, one: string, many = `${one}s`) {
  return `${count} ${count === 1 ? one : many}`;
}
export function money(amount: number, currency: string) {
  return new Intl.NumberFormat(undefined, {
    style: "currency",
    currency,
    maximumFractionDigits: 0,
  }).format(amount);
}
// Mirrors the backend's freshness window: three poll intervals, never under five minutes.
export function freshness(connection: Connection, now: number) {
  if (connection.enabled === false) return "paused";
  if (!connection.lastSuccessUtc) return "never";
  const window = Math.max(300, connection.pollSeconds * 3) * 1000;
  return now - Date.parse(connection.lastSuccessUtc) > window
    ? "stale"
    : "fresh";
}

export function buildCondition(
  snapshot: Snapshot,
  planning: PlanningSummary | "unavailable" | null,
  now: number,
) {
  const { connections, capabilities, plans } = snapshot;
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

  return { issues, verdict, signals, latestCollection };
}

export function buildUpcoming(
  snapshot: Snapshot,
  issues: Finding[],
  planning: PlanningSummary | "unavailable" | null,
  now: number,
) {
  const { disks, plans } = snapshot;
  const openPlans = plans.filter((p) => p.status !== "complete");
  type Upcoming = {
    iconTone?: "warn" | "bad";
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
    const sevenYearsAgo = new Date(now);
    sevenYearsAgo.setUTCFullYear(sevenYearsAgo.getUTCFullYear() - 7);
    const hasOlderDrive = aged.some((review) => {
      const purchased = disks.find(
        (disk) => disk.id === review.diskId,
      )?.purchasedUtc;
      return (
        purchased != null && Date.parse(purchased) < sevenYearsAgo.getTime()
      );
    });
    if (aged.length)
      upcoming.push({
        key: "age-review",
        iconTone: hasOlderDrive ? "bad" : "warn",
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

  return { upcoming, buckets };
}
