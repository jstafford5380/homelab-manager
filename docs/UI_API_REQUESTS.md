# UI → API requests

Data the UI needs but the API does not yet provide. Each entry names the screen, the gap, and a suggested contract. The UI shows nothing it cannot back with data, so these are what unlock the next round of forward-looking features.

## Overview: "Coming up" horizon (2026-11-01)

The redesigned overview (`src/Web/src/OverviewView.tsx`) builds a dated horizon from plan review dates, acknowledgement review dates, recorded warranty end dates, and `/planning/review` age reasons. It cannot yet show:

1. **Pool capacity runway.** The forecast exists per pool (`GET /connections/{id}/pools/{guid}/capacity`), but the overview has no pool list and would need N calls. Request: add to `/overview` a `capacityOutlook` array — `{ connectionId, poolGuid, poolName, usedFraction, projectedDateAtThreshold | null, thresholdFraction, basisDays, confidence, explanation }` — with `null` when the trend is insufficient. The UI will place it in the horizon ("tank reaches 80% in about 4 months") only when a projection exists.
2. **Host resource headroom (RAM, CPU, ARC).** Needed for "consider adding RAM / upgrading the board or CPU" guidance. Request: a read-only capability (e.g. from `reporting`/`system.info`) exposing sustained memory pressure and CPU saturation summaries per system, plus a planning-review reason kind such as `resource-headroom` with the evidence window and threshold used.
3. **Error-counter trend per drive.** Findings flag changes; the overview can't show "slowly worsening" before a finding fires. Request: in `/planning/review` reviews, an optional `trend` field — `{ metric, unit, windowDays, firstValue, lastValue, samples }` — for media counters (reallocated, pending, offline uncorrectable, CRC), so the UI can draw a small trend without inferring failure probability.

## Overview: response shape notes

- The overview type now reads `connectionId`, `diskId`, `firstSeenUtc` and `lastSeenUtc` from findings, and `enabled` from connections. These are already serialized; please keep them stable.
- The overview calls `GET /planning/review` once per refresh for drive age reasons, spares and budgets. If that gets expensive, a summary block on `/overview` would let the UI drop the extra call.
