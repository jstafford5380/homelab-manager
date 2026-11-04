import { relative, type Pool } from "./storageModel";

export function remainingTime(seconds: number) {
  const minutes = Math.max(1, Math.ceil(seconds / 60));
  if (minutes < 60) return minutes + " min";
  const hours = Math.floor(minutes / 60);
  if (hours < 24)
    return hours + " h" + (minutes % 60 ? " " + (minutes % 60) + " min" : "");
  return (
    Math.floor(hours / 24) +
    " d" +
    (hours % 24 ? " " + (hours % 24) + " h" : "")
  );
}
export function resilverEstimate(
  pool: Pool | undefined,
  stale: boolean,
): string | undefined {
  const scan = pool?.scan;
  return !stale &&
    scan?.scanFunction === "RESILVER" &&
    scan.scanState === "SCANNING" &&
    scan.scanRemainingSeconds != null &&
    scan.scanRemainingSeconds > 0
    ? "About " +
        remainingTime(scan.scanRemainingSeconds) +
        " remaining (TrueNAS estimate)"
    : undefined;
}
export function scanLine(pool: Pool, stale: boolean) {
  const s = pool.scan;
  const progress =
    s?.scanPercentage == null
      ? "progress unknown"
      : Math.round(s.scanPercentage) + "%";
  const line = !s?.scanFunction
    ? "No scrub recorded"
    : s.scanState === "SCANNING" || s.scanState === "PAUSED"
      ? (s.scanFunction === "RESILVER"
          ? "Resilvering · "
          : "Scrub in progress · ") +
        progress +
        (s.scanState === "PAUSED" ? " · paused" : "") +
        (!stale &&
        s.scanState === "SCANNING" &&
        s.scanRemainingSeconds != null &&
        s.scanRemainingSeconds > 0
          ? " · about " + remainingTime(s.scanRemainingSeconds) + " remaining"
          : "") +
        (s.scanStartedUtc ? " · started " + relative(s.scanStartedUtc) : "")
      : s.scanEndedUtc
        ? (s.scanFunction === "SCRUB" ? "Last scrub " : "Last resilver ") +
          relative(s.scanEndedUtc) +
          " · " +
          (s.scanErrors ?? "Unknown") +
          " errors"
        : "No scrub recorded";
  return line + (stale ? " (last known)" : "");
}
export function forecastLine(pool: Pool, stale: boolean, timezone: string) {
  const o = pool.capacityOutlook,
    threshold = Math.round((o?.thresholdFraction ?? 0.8) * 100);
  if (stale || o?.status === "stale" || o?.status === "paused")
    return "Forecast paused until this server reports again";
  switch (o?.status) {
    case "estimated":
      return o.projectedDateAtThreshold
        ? "At the current rate, " +
            threshold +
            "% full around " +
            new Date(o.projectedDateAtThreshold).toLocaleDateString("en-US", {
              month: "short",
              year: "numeric",
              timeZone: timezone,
            })
        : "No forecast available";
    case "threshold-reached":
      return "Already past " + threshold + "% full";
    case "not-growing":
      return "Not growing, so no fill date to show";
    case "insufficient":
      return "Not enough history yet for a forecast";
    default:
      return "No forecast available";
  }
}
