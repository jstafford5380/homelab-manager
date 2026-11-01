# UI → API requests

A handoff queue between UI work (Claude, `src/Web`) and API work (Codex, `src/Api`). When a screen needs data the API doesn't provide, the UI ships with what exists — never faking or inferring the missing data client-side — and the gap is logged here.

## How this works

- **Status** moves `requested → in progress → done`. Whoever changes it updates the date.
- **The suggested contract is a proposal.** The API side owns domain rules (units, freshness, what may be claimed). If the contract changes, record the final shape under **Implemented as**, or explain a pushback under **Notes**.
- **Done** means the endpoint or field is merged, covered by tests, and described under **Implemented as**. The UI side then wires it up and moves the entry to **Completed**.
- Add new entries at the end of **Open**, with the next free number. Never reuse a number.

## Open

### UR-1 · Pool capacity runway on the overview

- **Status:** done · 2026-11-01
- **Screen:** Overview → "Coming up" (`src/Web/src/OverviewView.tsx`)
- **Gap:** a capacity forecast exists per pool (`GET /connections/{id}/pools/{guid}/capacity`), but the overview has no list of pools and would need one call per pool.
- **Suggested contract:** add `capacityOutlook` to `GET /overview`:
  `{ connectionId, poolGuid, poolName, usedFraction, thresholdFraction, projectedDateAtThreshold | null, basisDays, confidence, explanation }`.
  Use `projectedDateAtThreshold: null` when the trend can't support a projection.
- **UI plan:** add "tank reaches 80% in about 4 months" to the horizon, only when a projection exists.
- **Implemented as:** `GET /api/v1/overview` now includes `capacityOutlook`, an array of:
  `{ connectionId, poolGuid, poolName, usedFraction: number | null, thresholdFraction: 0.8, projectedDateAtThreshold: UTC string | null, basisDays: 30, confidence, status, explanation, lastSampleUtc: UTC string | null, basisStartedUtc: UTC string | null, sampledDays, fit: number | null }`.
  `confidence` is `supported-trend` only when a date is available; otherwise `unavailable`. `status` uses the existing capacity endpoint's classifications (`estimated`, `threshold-reached`, `insufficient`, `stale`, `suppressed`, `not-growing`, `variable`, `distant`, `paused`, `unavailable`, `unverified`). The forecast uses the existing 30-day rules and current coverage checks. Dates are anchored to `lastSampleUtc`, not overview fetch time. `fit` is linear-regression R², not a failure probability.
  Covered by `CapacityOutlookTests` and the authenticated overview HTTP integration test; full backend suite: 765 passed.
- **Notes:** No extra NAS requests or methods. Four fixed database reads serve all pools; history is streamed with a global 300,000-sample cap. If exceeded, projections are withheld rather than calculated from truncated history. Historical/removed pools can appear with `unverified` coverage. `usedFraction` is the last valid saved capacity measurement, not an assertion of current freshness: display it with `status`/`lastSampleUtc`. `projectedDateAtThreshold` is null when already at the marker; use `status: threshold-reached` and/or the fractions for that case. UI should show a future runway only for `status: estimated` and a non-null date, retain the explanation, and avoid treating confidence as a numerical likelihood. No UI files changed; Claude can wire this up and move this entry to Completed.

### UR-2 · Host resource headroom (RAM, CPU, ARC)

- **Status:** requested · 2026-11-01
- **Screen:** Overview → "Coming up" and Planning
- **Gap:** no collection of memory pressure or CPU saturation, so there's nothing behind "consider more RAM / a CPU or board upgrade".
- **Suggested contract:** a read-only capability (for example from `reporting` / `system.info`, within the reviewed allowlist) giving per-system summaries of sustained memory pressure and CPU saturation over a stated window. Also add a `/planning/review` reason kind such as `resource-headroom` that includes the evidence window and threshold used.
- **UI plan:** add a planning reminder per system, plus headroom on the system cards.
- **Implemented as:** —
- **Notes:** this must stay read-only. If the installed TrueNAS version has no trustworthy source, say so here instead of approximating.

### UR-3 · Drive error-counter trends

- **Status:** requested · 2026-11-01
- **Screen:** Overview and Planning drive reviews
- **Gap:** findings fire when counters change, but nothing shows "slowly getting worse" before that happens.
- **Suggested contract:** an optional `trend` on each `/planning/review` review:
  `{ metric, unit, windowDays, firstValue, lastValue, samples: [{ atUtc, value }] }`
  for media counters (reallocated, pending, offline uncorrectable, CRC). Respect counter epochs: don't present a decrease as a repair.
- **UI plan:** a small trend line per drive, with no claims about failure probability.
- **Implemented as:** —
- **Notes:** —

## Contract notes (no action needed)

- The overview reads `connectionId`, `diskId`, `firstSeenUtc` and `lastSeenUtc` from findings, and `enabled` from connections. These are already serialized; please keep them stable.
- The overview calls `GET /planning/review` once per refresh. If that becomes expensive, a summary block on `/overview` would let the UI drop the extra call.

## Completed

_None yet._
