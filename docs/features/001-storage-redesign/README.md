# 001 · Storage redesign

- **Status:** ready · 2026-11-01
- **Mock:** [mock.html](mock.html). Open it in a browser and use the toolbar to switch State, Width, Theme and the `smart` flag.
- **Shared preview:** https://claude.ai/artifact/DvJnB3bpZr8whJFi7iNccp (the owner's design canvas; private unless shared)
- **Affected screens:** Storage (`/storage`, including `/storage?disk=<id>`). It replaces `StorageView`, `TopologyView` and the per-drive evidence screens.

## Goal

For each server, the owner can see at a glance what pools exist, which drives are in them, where each drive physically sits, and whether any drive needs attention. They can open one drive to check its health, record what it cost and when it went in, and see roughly when it's due for replacement under their own plan. This is a home lab: everything is about maintaining hardware and planning purchases, not auditing evidence.

## Design summary

- **One server at a time.** Server tabs at the top, each with a status dot and a one-line summary ("9 drives · 1 to watch" or "Out of date · 2 days ago"). Servers are never mixed on one screen.
- **Chunked to stay easy to read (Miller's law).** The server header has 4 facts. Each pool card has 3 lines of context, then a table with 6 columns. The drive panel has 4 groups. Everything else is behind a disclosure or in the panel.
- **The drive panel** opens from the drive's name. It's a right-hand side panel on desktop and tablet, and a full screen with a back link on phones. Groups: Health, Where it is, Cost & lifespan, History. Actions sit in a bar at the bottom.
- **Status uses words plus colour,** never colour alone: Healthy, Watch, Act now, Missing, Spare, Unknown, Last known.
- **Lifespan is the owner's plan, not a prediction:** "Year 4 of 5 · replace around Mar 2028", based on the configured review age.
- **SMART-based features (001.17–001.20) sit behind the `smart` flag** until the SSH adapter ships. With the flag off, the page is complete and correct using ZFS, TrueNAS alerts and cached temperatures.

Rejected alternatives:
- **All servers on one long page:** intermingles servers, which the owner explicitly doesn't want.
- **A per-drive "evidence" screen:** audit-level detail the owner doesn't need.
- **Inferring product names silently:** a guessed name is always editable, and the reported model is always visible next to it.

## Features

| ID | Feature | Depends on | Flag | Status |
| --- | --- | --- | --- | --- |
| 001.01 | Server switcher (tabs with status dot and summary line) | — | — | ready |
| 001.02 | Server header: freshness, last collected, 4 facts, out-of-date banner | 001.01 | — | ready |
| 001.03 | Pool cards: name, status, layout with redundancy in words, usage bar | 001.01 | — | ready |
| 001.04 | Scrub / resilver status line on each pool | 001.03 | — | ready |
| 001.05 | Pool fill forecast line (uses UR-1 `capacityOutlook`) | 001.03 | — | ready |
| 001.06 | "What's using space" dataset disclosure | 001.03 | — | ready |
| 001.07 | NAS alerts chip in the server header | 001.02 | — | ready |
| 001.08 | Drive table per pool, plus a "Not in a pool" card | 001.03 | — | ready |
| 001.09 | Drive status from ZFS state, ZFS errors, findings and alerts | 001.08 | — | ready |
| 001.10 | Drive panel: header, Where it is, ZFS-based Health, action bar | 001.08 | — | ready |
| 001.11 | Drive nicknames (suggested, editable, raw model always shown) | 001.10 | — | ready |
| 001.12 | Bay confirmation (table mark plus "I've checked it's here") | 001.10 | — | ready |
| 001.13 | Cost & lifespan: view, edit form, "estimated" flag, service-plan bar | 001.10 | — | ready |
| 001.14 | History list plus "Add note" | 001.10 | — | ready |
| 001.15 | Linked plan / "Plan a replacement" | 001.10 | — | ready |
| 001.16 | "Mark as replaced" | 001.10 | — | ready |
| 001.17 | SMART adapter: `smartctl` over restricted SSH (backend) | — | `smart` | ready |
| 001.18 | SMART health details and freshness line | 001.17, 001.10 | `smart` | ready |
| 001.19 | Error-counter trend chart | 001.18 | `smart` | ready |
| 001.20 | "Import SMART report" action in the drive panel | 001.10 | `smart` | ready |

## States

The mock's **State** switcher covers each row. Server tabs inside the mock also switch servers directly.

| State | When it happens | What the user sees | Fixture |
| --- | --- | --- | --- |
| Normal | Server reporting, at least one pool | TrueNAS: two pools, one drive on Watch, a spare not in a pool, 2 NAS alerts | `fixtures/server-truenas.json` |
| Out of date | Last successful collection is older than the freshness window | Amber banner; statuses read "Last known"; forecast paused; facts show "Unknown" for Needs a look | `fixtures/server-backup.json` |
| Partly unknown / scan running | Pool scrub in progress; a drive without SMART (flag on) | Proxmox: "Scrub in progress · 72%"; a drive with status Unknown and an explanation | `fixtures/server-proxmox.json` |
| Empty | No servers enrolled | "No servers yet" with a link to Settings | `fixtures/servers-empty.json` |
| Loading | First load, nothing cached | Skeleton tabs and one skeleton pool card; no numbers | — |
| Unavailable | Storage request failed | Error banner with Retry. If data was loaded earlier in the session, it stays visible under the banner, labelled as last loaded | — |
| `smart` flag off | SMART adapter not shipped | SMART tiles, trend, firmware and Import button are absent. Health shows pool member state, ZFS errors and temperature | same fixtures, SMART fields ignored |
| Drive panel open | User picks a drive | Side panel (desktop/tablet) or full screen (phone) | `fixtures/drive-za1xyz99.json` |
| Mobile (≤ 700px) | Narrow viewport | Tabs scroll sideways, facts sit 2×2, tables become drive cards, and the panel becomes a full screen with "← server · pool" | — |

## Copy

Use these strings as written. `{…}` marks data.

- Page: title "Storage", subtitle "Pools and drives, one server at a time."
- Tab summary line: "{n} drives · all healthy", "{n} drives · {k} to watch", or "Out of date · {relative time}".
- Server header: "{host} · last collected {relative time}". Freshness pill: "Reporting", "Out of date", "Paused" or "No collection yet".
- Out-of-date banner: "**Can't reach this server right now.** Everything below is from the last successful check, {relative time}. Drive status may have changed since."
- Facts: "Pools", "Drives", "Raw capacity", "Needs a look" (shows "Unknown" when the server is out of date).
- Pool layout: "{RAIDZ2|Mirror|…} · {n} drives · survives {k} drive loss(es)". If redundancy is unknown: "{layout} · {n} drives".
- Pool status pill: "Healthy", "Needs a look", "Degraded", "Partly unknown", "Last known: healthy".
- Scrub line:
  - "Last scrub {relative} · {n} errors"
  - "Scrub in progress · {pct}% · started {relative}"
  - "Resilvering · {pct}%"
  - "No scrub recorded"
  - when the server is out of date, append " (last known)" to any of these
- Forecast line, from `capacityOutlook.status`:
  - `estimated` with a date: "At the current rate, {threshold}% full around {Mon YYYY}"
  - `threshold-reached`: "Already past {threshold}% full"
  - `not-growing`: "Not growing, so no fill date to show"
  - `insufficient`: "Not enough history yet for a forecast"
  - `stale` or `paused`: "Forecast paused until this server reports again"
  - anything else: "No forecast available"
  - never show a date unless the status is `estimated`
- Disclosure: "What's using space". If dataset reads are turned off: "Dataset usage is turned off. Turn it on in Settings."
- "Not in a pool" card: subtitle "Spares, boot drives and anything unassigned".
- Table headers: Status, Drive, Capacity, Serial, Location, In service. A missing install date shows the link "Add install date".
- Bay marks: "Confirmed" (with a check icon) and "Not confirmed".
- Panel header: "{cap} · {type} · in pool {pool}" or "· not in a pool"; "Reported model: {raw model}"; "Rename" or "Give it a name".
- Why box (whenever the status isn't Healthy): "**Why "{status}":** {one or two plain sentences}". For example:
  - "Pending sectors went from 0 to 8 this week. This is often the first sign of wear. Make sure you have a spare and a recent backup."
  - with the flag off: "TrueNAS reported 8 unreadable (pending) sectors on this drive today…"
- Health freshness line:
  - flag off: "From ZFS and TrueNAS. No SMART details yet."
  - flag on, normal: "SMART read {relative} · last self-test {result} {relative}"
  - flag on, sleeping drive: "Asleep, so it was skipped to avoid waking it. Last SMART reading {relative}."
  - flag on, out-of-date server: "Last SMART reading {relative}, before this server stopped reporting."
  - flag on, no SMART support: "This drive doesn't provide SMART data."
- Location confirmation: "You confirmed this location.", or "Not yet confirmed. Check the physical label before pulling this drive." with the button "I've checked it's here".
- Lifespan bar: "Year {n} of {plan}", plus "Replace around {Mon YYYY}" or "Replacement due". Caption: "Based on your {plan}-year replacement plan, not a failure prediction."
- Cost line: "${cost} · about ${per year} per year so far", or "Not recorded".
- Edit form labels: "Price paid (USD)", "Purchase date", "Install date", "Warranty ends", "These dates are estimates", "Notes". Buttons: "Cancel" and "Save".
- Plan link: "**Replacement planned** · {budget} · {ordered | not ordered yet}", then "Open plan".
- Actions: "Plan a replacement" (hidden when a plan exists), "Mark as replaced", "Import SMART report" (flag `smart`).
- Empty state: heading "No servers yet", body "Connect a NAS or server in Settings and its pools and drives will appear here.", button "Go to Settings".
- Unavailable banner: "**Storage couldn't be loaded.** {error}." then "Showing what was loaded {relative}." or "Nothing has been loaded yet.", and the button "Retry".

## Drive status rules (001.09)

Evaluate top to bottom. The first match wins.

| Status | Tone | Rule |
| --- | --- | --- |
| Last known: {status} | neutral | Server is out of date: show the last computed status, prefixed |
| Missing | warn | Drive absent from the latest inventory (`present = false`) |
| Act now | bad | ZFS member FAULTED, UNAVAIL or REMOVED; or an open `critical` finding on the drive; or SMART overall failed (flag on) |
| Watch | warn | ZFS read/write/checksum errors above 0 since the last scrub; or an open `urgent`/`review` finding on the drive; or a native NAS alert naming the drive |
| Unknown | neutral | Flag on, and the drive provides no SMART data while nothing else applies |
| Spare | neutral | Not a member of any pool and nothing above applies |
| Healthy | ok | Otherwise |

Pool status: "Degraded" (bad) when ZFS reports the pool degraded; "Needs a look" (warn) when any member is Watch or Act now; "Partly unknown" (neutral) when any member is Unknown; "Last known: healthy" when the server is out of date; otherwise "Healthy".

## Data needs

Fixtures show a proposed shape. Codex owns the real contract and may restructure it, for example as one `/storage/servers/{id}` call.

| Feature | Need | Proposed field(s) | Notes |
| --- | --- | --- | --- |
| 001.01 | Server list with freshness | `id, name, host, freshness, lastSuccessUtc, driveCount, attentionCount` | Freshness uses the existing window (3 poll intervals, minimum 5 minutes) |
| 001.02 | Facts | `poolCount, driveCount, rawCapacityBytes, attentionCount` | Raw capacity is the sum of drive sizes. `attentionCount` is null when out of date |
| 001.03 | Pool basics | `guid, name, status, layout, memberCount, toleratedFailures, usedBytes, sizeBytes` | From `pool.query` and the existing `PoolRedundancy`. `toleratedFailures` is null when unknown |
| 001.04 | Scan | `scan: { function, state, percentage, startedUtc, endedUtc, errors }` | From the existing pool snapshot / `ScanOverview` |
| 001.05 | Forecast | the existing `capacityOutlook` entry | Show a date only for `status: estimated` |
| 001.06 | Datasets | `datasets: [{ name, usedBytes }]`, `datasetsEnabled` | From the optional `pool.dataset.query`. Top 6 by size, rest summed as "Other" |
| 001.07 | NAS alerts | `alerts: [{ title, level, seenUtc }]` | From `alert.list`. Hide the chip when there are none |
| 001.08 | Drive row | `id, status, title, rawModel, sizeBytes, type, rpm, serial, bay, enclosure, slot, bayConfirmed, installedUtc, poolGuid` | **New from `disk.query`:** `size`, `type`, `rotationrate`, `enclosure.number/slot` (nullable on DIY hardware). Hide the enclosure/slot line when null |
| 001.09 | Status inputs | ZFS member state and error counters, open findings, alerts naming the drive | Rules above. Return `status` and a short `why` |
| 001.11 | Nickname | `nickname` (nullable), `suggestedName` | Suggested on the server from a small model-prefix table (WDC/WD → WD, ST → Seagate, and so on), falling back to the raw model. Never overwrite a user's nickname |
| 001.12 | Bay confirmation | existing bay verification on `/disks/{id}/metadata` | Show "Confirmed" only while the current identity matches the confirmation |
| 001.13 | Cost & lifespan | `purchaseCost, currency, purchasedUtc, installedUtc, warrantyEndUtc, notes, datesEstimated`, `servicePlanYears` | Existing `/disks/{id}/acquisition` and `/metadata`. "Estimated" maps to the existing confidence fields. `servicePlanYears` = `OperatingSettings.DriveAgeReviewYears` |
| 001.14 | History | `history: [{ atUtc, kind, text }]` | Merged from acquisition, replacements, maintenance records and finding opened/resolved events. "Add note" creates a maintenance record |
| 001.15 | Linked plan | the open plan with matching `DiskId` (`budget, currency, status`) | "Plan a replacement" opens the existing plan form prefilled with the drive |
| 001.16 | Replacement | existing `/replacements` | Prefill the old drive; the new drive gets today's install date |
| 001.17 | SMART source | see **SMART adapter** below | Per-server opt-in |
| 001.18 | SMART details | `smart: { overall, readUtc, skippedAsleep, powerOnHours, temperatureC, pendingSectors, reallocatedSectors, nvmeWearPercent, lastSelfTest: { type, result, atUtc }, firmware }` | Every field nullable. Use cached `disk.temperatures` for temperature when the flag is off |
| 001.19 | Trend | `trend: { metric, points: [{ atUtc, value }] }` for counters that changed in the last 8 weeks | Respect counter reset epochs; never show a decrease as a repair |
| 001.20 | Import | existing SMART report import | Entry point only |

## SMART adapter (001.17)

The owner has approved reading SMART data with `smartctl` over SSH. This adds a narrow shell channel next to the read-only TrueNAS API, so the restriction has to be enforced **on the server**, not only in app code:

- A dedicated, key-only user (for example `homelab-monitor`), with no password login, accepted only from the app's address.
- A forced command on the key: a wrapper script that allows exactly `smartctl --scan` and `smartctl --json -x -n standby /dev/<disk>`, where `<disk>` is validated against the scan output. Everything else is refused.
- Narrow sudo: only that `smartctl` command, via TrueNAS's per-user "allowed sudo commands". Check on 24.10 whether the UI accepts forced-command options on keys, and document the setup if it doesn't.
- No PTY, no port or agent forwarding, no free-form commands from the app. Never start self-tests; only read results.
- `-n standby` skips sleeping drives. Report those as `skippedAsleep` with the last good reading, and never wake them.
- Schedule: every 6–12 hours per server. Reuse the existing SSH tunnel settings where present.
- Update `docs/NAS_READ_MANIFEST.md` and the README's NAS safety section to describe the new boundary honestly.
- Keep the `smart` flag off until this is merged and tested. Setting up a server's health-data connection needs a Settings screen, which is a separate future package. Until then, the adapter can be configured by whatever minimal means Codex chooses, behind the flag.

## Interaction and accessibility

- **Keyboard path:** server tabs (buttons with `aria-pressed`), then each pool's disclosure, then drive name buttons in table order. A drive name opens the panel and moves focus to the panel heading. Esc and the close button return focus to the drive name that opened it.
- **The panel is a modal dialog on desktop** (`role="dialog"`, `aria-modal`, labelled by the drive title, focus trapped). On phones it's a page view with a back link, not a dialog.
- **Announcements:** the out-of-date and unavailable banners use `role="status"`, and a successful save announces "Saved". No live region on the 30-second refresh.
- **Status is never colour alone.** Pills carry the word, and dots always sit next to a text value.
- **Charts:** the usage bars, lifespan bar and trend chart have `role="img"` with a text label giving the values.
- **Touch targets** are at least 44px on phones.
- **Links:** "Open plan" goes to Planning, focused on that plan. "Go to Settings" goes to the Settings connection section. `/storage?disk=<id>` opens that drive's server with the panel open.

## Out of scope

- A Proxmox or generic-Linux collector. The mock shows a Proxmox server to prove the layout works for non-TrueNAS hosts, but collecting from it is a future package.
- The Settings screen for setting up SMART over SSH (future package).
- A "Needs identifying" flow for drives with conflicting identities. Until it's designed, keep identity conflicts as findings: they surface as Watch with a why line.
- Shared-hardware relationships, identity merge/split history, collection-attempt history, per-source coverage tables, evidence authors, raw self-test logs, and pool scan/daily history pages. These are no longer linked from Storage. Codex decides whether to delete the components or keep them unlinked.

## Open questions

- None blocking. If `disk.query` on the installed 24.10 patch doesn't return `enclosure` or `rotationrate`, hide those values; the design already handles both being missing.

## Acceptance checklist

- [ ] Every state in the table is implemented and reachable.
- [ ] Matches the mock at desktop (≥ 1100px), tablet (~900px), and mobile (390px), in light and dark.
- [ ] No horizontal page scroll at 390px. Wide tables become cards rather than scrolling.
- [ ] Axe passes WCAG 2.1 AA in light and dark (extend `src/Web/e2e`).
- [ ] Unknown, stale, and unsupported data look different from healthy data.
- [ ] No claims beyond the evidence (no failure dates or probabilities).
- [ ] Every flagged feature is completely absent when its flag is off, and the page still reads correctly without it.
- [ ] Feature-specific checks:
  - [ ] 001.01 Switching servers never shows another server's pools or drives, even briefly.
  - [ ] 001.02 An out-of-date server shows the banner and "Unknown" for Needs a look.
  - [ ] 001.05 A date appears only for `capacityOutlook.status = estimated`.
  - [ ] 001.06 With dataset reads off, the disclosure explains how to turn them on and shows no numbers.
  - [ ] 001.08 Enclosure/slot is hidden when `disk.query` doesn't report it.
  - [ ] 001.09 The status rules table is implemented in order, with unit tests for each row.
  - [ ] 001.11 The raw model is visible wherever a nickname or suggested name is shown in the panel.
  - [ ] 001.12 "Confirmed" disappears if the drive's identity changes after confirmation.
  - [ ] 001.13 The lifespan bar follows `DriveAgeReviewYears` and always shows the "not a failure prediction" caption.
  - [ ] 001.17 The wrapper refuses every command except the two allowed ones (tested), and sleeping drives are never woken.
  - [ ] 001.19 Counter resets never draw as decreases.
  - [ ] `/storage?disk=<id>` deep links still work, and old deep links don't 404.

## Implementation notes

_Codex: one entry per feature ID as it lands._

### 001.01

—

## Change log

| Date | Change | By |
| --- | --- | --- |
| 2026-11-01 | Package created at lock-in. Loading, empty and unavailable states, the Act now / Missing statuses, and the status rules were written down at lock-in, following the canvas's existing patterns. | Claude |
