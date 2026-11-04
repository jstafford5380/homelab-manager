import { useEffect, useState } from "react";
import { Calendar, CircleAlert, TriangleAlert } from "lucide-react";
export type PlanningReason = {
  kind: string;
  detail: string;
  findingId?: string | null;
  summary?: string | null;
  endsUtc?: string | null;
  purchasedUtc?: string | null;
  confidence?: string | null;
};
export type SpareFit = {
  status: string;
  assetId?: string | null;
  assetName?: string | null;
  validUntilUtc?: string | null;
};
export type DriveReview = {
  diskId: string;
  model: string;
  label?: string | null;
  serial: string;
  location?: string | null;
  priority: string;
  priorityKey?: string;
  reasons: PlanningReason[];
  openPlan?: { id: string; status: string } | null;
  spareFit: SpareFit;
};
export const priority = (review: DriveReview) =>
  review.priorityKey ||
  ({
    "Review media evidence": "media",
    "Review observed issue": "issue",
    "Prepare ahead": "prepare",
  }[review.priority] ??
    review.priority);
export const priorities = {
  media: {
    label: "Media evidence",
    tone: "bad",
    Icon: TriangleAlert,
    action: "Plan replacement",
  },
  issue: {
    label: "Observed issue",
    tone: "warn",
    Icon: CircleAlert,
    action: "Plan a review",
  },
  prepare: {
    label: "Prepare ahead",
    tone: "info",
    Icon: Calendar,
    action: "Add to plan",
  },
};
export const money = (value: number, currency: string) =>
  new Intl.NumberFormat(undefined, { style: "currency", currency }).format(
    value,
  );
// Calendar days in the configured display timezone, including across DST boundaries.
export function calendarDay(value: string | number, timezone: string) {
  const parts = new Intl.DateTimeFormat("en-CA", {
    timeZone: timezone,
    year: "numeric",
    month: "2-digit",
    day: "2-digit",
  }).formatToParts(new Date(value));
  const get = (key: string) => Number(parts.find((p) => p.type === key)!.value);
  return Date.UTC(get("year"), get("month") - 1, get("day")) / 86400000;
}
export const dueDays = (due: string, timezone: string, now = Date.now()) =>
  calendarDay(due, timezone) - calendarDay(now, timezone);
export const shortDate = (value: string, timezone: string, year = false) =>
  new Date(value).toLocaleDateString(undefined, {
    timeZone: timezone,
    month: "short",
    ...(year ? { year: "numeric" as const } : { day: "numeric" as const }),
  });
export function focusPlan(id: string) {
  const card = document.getElementById(`plan-${id}`);
  card?.focus();
  card?.scrollIntoView({ block: "center", behavior: "instant" });
}

export function usePlanningClock() {
  const [now, setNow] = useState(Date.now);
  useEffect(() => {
    const timer = setInterval(() => setNow(Date.now()), 10000);
    return () => clearInterval(timer);
  }, []);
  return now;
}
