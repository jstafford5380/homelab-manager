import {useDisplayFormat} from "../../lib/displayPreferences";

import type {ScanTimingFields} from "./scanModel";
function duration(seconds: number) {
  if (seconds < 60) return "less than a minute";
  if (seconds < 3600) return `about ${Math.round(seconds / 60)} min`;
  if (seconds < 86400) return `about ${(seconds / 3600).toFixed(1)} hours`;
  return `about ${(seconds / 86400).toFixed(1)} days`;
}
export function ScanTiming({ scan }: { scan: ScanTimingFields }) {
  const { time } = useDisplayFormat();
  return <details className="scan-timing">
    <summary>Source scan timing</summary>
    <dl>
      <dt>Started</dt><dd>{scan.scanStartedUtc ? time(scan.scanStartedUtc) : "Unknown"}</dd>
      <dt>Ended</dt><dd>{scan.scanEndedUtc ? time(scan.scanEndedUtc) : "Not reported"}</dd>
      <dt>Paused</dt><dd>{scan.scanPausedUtc ? time(scan.scanPausedUtc) : "Not reported"}</dd>
      <dt>Remaining at pool read</dt><dd>{scan.scanRemainingSeconds == null ? "Unknown" : duration(scan.scanRemainingSeconds)}</dd>
    </dl>
    <p className="muted">NAS-reported times. Remaining time is approximate, may change while paused, and is not a countdown or completion promise.</p>
  </details>;
}
