# 004 · Dashboard layout

- **Status:** ready · 2026-11-03
- **Mock:** [mock.html](mock.html). Use the toolbar to switch State, Width and Theme, e.g. `?state=fleet&width=tablet&theme=dark`.
- **Shared preview:** https://claude.ai/artifact/7z7esRrBJvQa4XzemPsKpi (the exploration version; `mock.html` is the final one)
- **Affected screens:** **Overview** (`/`): everything below the Current condition hero. The hero itself changes only in how its tiles wrap (004.05) and whether the Findings tile is a link (004.01).

## Goal

The Overview should look complete whether there are zero findings or twenty, and one system or several. Today the layout is two fixed columns. When nothing needs attention, the left column shrinks to one line while the right column holds three sections, and a lone system card sits at the bottom of a four-column grid. The page looks broken exactly when things are fine.

## Design summary

- **The main column always has the system in it.** Main column order: Needs your attention (only when there are findings), then Scrub & resilver reports (only while a report is in progress), then Systems. The rail on the right holds Coming up and Readiness. Systems moves out of its full-width row at the bottom into the main column, so the main column never ends short.
- **No empty attention card.** With no findings, the "Needs your attention" section is not rendered. The hero's Findings tile already says "None open", and the headline already explains whether coverage is complete. The Findings tile stops being a link in that state, because there is nothing to scroll to.
- **One system gets a full card.** With exactly one system, its card shows the same four stats as today, plus a **Sources** list: each source the app reads from that system, with its status. This explains the "monitoring is incomplete" headline in place, and it fills the column with information the owner can act on. Gaps sort first.
- **Several systems get one row each.** With two or more systems, Systems becomes a single card with one row per system (name, status pill, last collected, drives, open findings, source gaps). A table of rows never leaves a hole the way an odd number of cards in a grid does. Below 700px each row becomes a two-column label/value block.
- **The quiet scan message becomes one line in Readiness.** "No in-progress scan…" was a whole section of grey text. It's now the third row of the Readiness card, keeping today's caveat. While a scan report is in progress, the full report card (today's `ScanOverview` content) appears in the main column above Systems, and the Readiness row says where it is.
- **Tablet puts the rail side by side.** Between 700 and 1100px the body is one column, and Coming up and Readiness sit next to each other under it instead of stacking into a long tail.
- **Tiles wrap evenly.** The five hero tiles stay in one row down to 700px. Below that they're two columns, and Total investment spans the last row, so no tile sits alone next to a gap.

Rejected alternatives:
- **Masonry or CSS columns for all sections.** Rejected: reading order jumps between columns, and the order changes as data changes.
- **A sticky rail.** Rejected: when the rail is taller than the viewport, its bottom becomes unreachable until the main column ends.
- **Keeping a one-line "No open findings" card.** Rejected: it repeats the hero, and it was the source of the gap.
- **Merging Coming up and Readiness into one card.** Rejected: they answer different questions (what's due, and how prepared you are), and Coming up can grow long.

## Features

| ID | Feature | Depends on | Flag | Status |
| --- | --- | --- | --- | --- |
| 004.01 | Adaptive body: attention section only when there are findings; main column order attention → active scans → Systems; rail = Coming up, Readiness; Findings tile not a link when there are no findings | — | — | ready |
| 004.02 | Single-system card: today's stats plus a Sources list with a status pill per source, gaps first, "Review coverage" link | 004.01 | — | ready |
| 004.03 | System rows for two or more systems, with a stacked label/value layout below 700px | 004.01 | — | ready |
| 004.04 | Readiness gains the scan line; in-progress scan reports move into the main column above Systems | 004.01 | — | ready |
| 004.05 | Responsive: hero tiles 5 across down to 700px, then 2 columns with Total investment spanning; rail two-up between 700 and 1100px | 004.01 | — | ready |

## States

| State | When it happens | What the user sees | Fixture |
| --- | --- | --- | --- |
| Calm · 1 system | One connection, no findings, no scan in progress | Hero; main column is the Media NAS card with stats and Sources (3 gaps first); rail is Coming up and Readiness, whose third row reads "No scrub or resilver in progress" | `fixtures/calm.json` |
| Findings · 1 system | One connection, two findings (one acknowledged) | Needs your attention leads the main column, then the system card. The rail is shorter than the main column, which is expected | `fixtures/findings.json` |
| Several systems | Three connections, one out of date, one finding, one scrub in progress | Attention, then the scrub report card, then Systems as three rows. Backup NAS shows "Out of date". Readiness row says "1 scan report in progress" | `fixtures/fleet.json` |
| Partly unavailable | Overview refresh failed (last-known data shown); planning and scan summary unavailable | Today's refresh warning above the hero. Coming up shows "Drive age reminders are unavailable right now." Readiness shows "Spares unknown" and "Scan summary unavailable" | `fixtures/partial.json` |
| Loading planning | Overview loaded, planning still loading | Coming up shows "Checking planning reminders…"; Readiness shows "Spares unknown" | `fixtures/loading.json` |
| Empty | No connections | Hero and the "Meet your NAS" card only. Retained scan reports still show below it, as today | `fixtures/empty.json` |
| Tablet (700–1100px) | | One column; Coming up and Readiness side by side under the main column | — |
| Mobile (≤ 700px) | | Everything stacks. Hero tiles 2 columns with Total investment spanning. Stats 2×2. Sources one column. System rows become label/value blocks | — |

## Copy

New or moved copy only. Everything else is today's text.

- **Sources** (004.02): heading "Sources", link "Review coverage" (to `/storage`, same as the Coverage tile).
- **Source labels** (004.02), proposed for `capabilities[].name`: `system_identity` System identity · `inventory` Drive inventory · `pools` Pools · `datasets` Datasets · `alerts` NAS alerts · `cached_temperatures` Drive temperatures · `smart` SMART health · `recovery` Restore evidence. Anything else falls back to today's `capabilityLabel()`.
- **Source status pills** (004.02): `supported` Reporting (ok) · `disabled` Turned off · `unsupported` Unsupported · `not_reported` / `missing` Not reported (all neutral, dashed) · `unavailable` Unavailable · `denied` Access denied · `stale` Out of date (all warn). Every pill has an icon as well as a label.
- **Readiness scan row** (004.04), keeping today's caveats:
  - None in progress: "No scrub or resilver in progress" / "In the latest retained pool snapshots. This does not establish that all pools were checked." + "Review storage history".
  - In progress: "1 scan report in progress" (pluralised) / "Shown under Scrub & resilver reports."
  - Summary unavailable: "Scan summary unavailable" / "Review storage history and collection coverage."
- **Readiness spares** when planning is loading or unavailable: "Spares unknown" / "Planning data isn't available right now." (Today shows "Spares unknown" with "Recorded in inventory", which isn't true when nothing loaded.)
- **System rows** (004.03): column headers System, Status, Last collected, Drives, Open findings, Source gaps. The sub-line under each name is the connection's `status` text, truncated with an ellipsis. The full text is in a `title` attribute and on the connection page.

## Data needs

No API changes. Every field below is already on `/api/v1/overview` or `/api/v1/planning/review`.

| Feature | Need | Field(s) | Notes |
| --- | --- | --- | --- |
| 004.01 | Whether there are findings | `findings` (open + acknowledged, as today's `issues`) | Same filtering as today |
| 004.02 | Sources per system | `capabilities[]` where `connectionId` matches: `name`, `status` | Gap = `status !== "supported"`, today's Source gaps rule |
| 004.02, 004.03 | System stats | `connections[]`, `disks[]`, existing `freshness()` | Unchanged |
| 004.04 | Scan in progress or unavailable | `scanReports`: non-empty array, empty array, or not valid (unavailable) | Today's `ScanOverview` rules |
| 004.04 | Spares, budgets | `planning` (`null` loading, `"unavailable"`, or the summary) | Unchanged |

Fixtures are the overview fields this layout reads. `_mock` holds values that existing code already derives (hero, Coming up items, drive counts, refresh error). They're there only so the mock can render.

## Interaction and accessibility

- **Focus order** follows the visual order: hero tiles, then attention, then scan reports, then Systems, then Coming up, then Readiness. Rearrange the DOM, not with CSS `order`, so screen-reader order matches.
- **Headings** stay `h2` per section. The system card name is `h3`. The Sources list has `aria-label="Sources for <system name>"`.
- **System rows** use a real `<table>` (or `role="table"` with rows and cells) so the column headers are announced. Below 700px the header row is visually hidden but stays available to assistive tech, and each cell shows its own label.
- **The Findings tile when there are no findings** is a plain element, not a link, so there's no dead link in the tab order.
- **Status is never colour alone.** Source pills and system pills carry an icon and a label. Neutral pills keep the dashed border.
- **Links:** the system name (in the card and in rows) links to `/settings/connections/<id>`. "Review coverage" goes to `/storage`. "Review storage history" goes to `/storage`.

## Out of scope

- Changing what the hero computes (headline, tiles, investment).
- New sections or data (recent activity, capacity outlook). They can be added later in the main column under Systems without changing this structure.
- Coming up content and bucketing.

## Open questions

None.

## Acceptance checklist

- [ ] Every state in the table is implemented and reachable.
- [ ] Matches the mock at desktop (≥ 1100px), tablet (~900px), and mobile (390px), in light and dark.
- [ ] No horizontal page scroll at 390px.
- [ ] Axe passes WCAG 2.1 AA in light and dark (extend `src/Web/e2e`).
- [ ] Unknown, stale, and unsupported data look different from healthy data.
- [ ] No claims beyond the evidence (no failure dates or probabilities).
- [ ] Feature-specific checks:
  - [ ] 004.01: with no findings, no "Needs your attention" heading or card is rendered, and the Findings tile is not focusable. With findings, the section is first in the main column.
  - [ ] 004.01: in the calm one-system state at 1400px wide, the main column and rail end within about 120px of each other.
  - [ ] 004.02: with one system, Sources lists every capability for that connection, gaps first. The Source gaps count equals the number of non-"supported" rows.
  - [ ] 004.03: with two or more systems, Systems is one card of rows. "Last collected" doesn't wrap at 1100px or above. At 390px each row shows its labels.
  - [ ] 004.04: with no scan in progress there is no Scrub & resilver section, and the Readiness row shows the caveat. With a report in progress, the full report card is above Systems. With an invalid or missing summary, the row reads "Scan summary unavailable".
  - [ ] 004.04: retained scan reports still render under "Meet your NAS" when there are no connections.
  - [ ] 004.05: at 390px the hero tiles are two columns and Total investment spans both. Between 700 and 1100px, Coming up and Readiness sit side by side.

## Implementation notes

_Codex: one entry per feature ID as it lands: final endpoints and fields, components touched, deviations from the mock or fixtures and why, and test coverage._

### 004.01

—

### 004.02

—

### 004.03

—

### 004.04

—

### 004.05

—

## Change log

| Date | Change | By |
| --- | --- | --- |
| 2026-11-03 | Package created | Claude |
