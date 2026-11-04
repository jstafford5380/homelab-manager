# 001 · Storage redesign

- **Status:** in progress · 2026-11-01
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
| 001.01 | Server switcher (tabs with status dot and summary line) | — | — | in progress |
| 001.02 | Server header: freshness, last collected, 4 facts, out-of-date banner | 001.01 | — | in progress |
| 001.03 | Pool cards: name, status, layout with redundancy in words, usage bar | 001.01 | — | in progress |
| 001.04 | Scrub / resilver status line on each pool | 001.03 | — | in progress |
| 001.05 | Pool fill forecast line (uses UR-1 `capacityOutlook`) | 001.03 | — | in progress |
| 001.06 | "What's using space" dataset disclosure | 001.03 | — | in progress |
| 001.07 | NAS alerts chip in the server header | 001.02 | — | in progress |
| 001.08 | Drive table per pool, plus a "Not in a pool" card | 001.03 | — | in progress |
| 001.09 | Drive status from ZFS state, ZFS errors, findings and alerts | 001.08 | — | in progress |
| 001.10 | Drive panel: header, Where it is, ZFS-based Health, action bar | 001.08 | — | in progress |
| 001.11 | Drive nicknames (suggested, editable, raw model always shown) | 001.10 | — | in progress |
| 001.12 | Bay confirmation (table mark plus "I've checked it's here") | 001.10 | — | in progress |
| 001.13 | Cost & lifespan: view, edit form, "estimated" flag, service-plan bar | 001.10 | — | in progress |
| 001.14 | History list plus "Add note" | 001.10 | — | in progress |
| 001.15 | Linked plan / "Plan a replacement" | 001.10 | — | in progress |
| 001.16 | "Mark as replaced" | 001.10 | — | in progress |
| 001.17 | SMART adapter: `smartctl` over restricted SSH (backend) | — | `smart` | in progress |
| 001.18 | SMART health details and freshness line | 001.17, 001.10 | `smart` | in progress |
| 001.19 | Error-counter trend chart | 001.18 | `smart` | in progress |
| 001.20 | "Import SMART report" action in the drive panel | 001.10 | `smart` | in progress |

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

- [x] Every state in the table is implemented and reachable.
- [x] Matches the mock at desktop (≥ 1100px), tablet (~900px), and mobile (390px), in light and dark.
- [x] No horizontal page scroll at 390px. Wide tables become cards rather than scrolling.
- [x] Axe passes WCAG 2.1 AA in light and dark (extend `src/Web/e2e`).
- [x] Unknown, stale, and unsupported data look different from healthy data.
- [x] No claims beyond the evidence (no failure dates or probabilities).
- [x] Every flagged feature is completely absent when its flag is off, and the page still reads correctly without it.
- [x] Feature-specific checks:
  - [x] 001.01 Switching servers never shows another server's pools or drives, even briefly.
  - [x] 001.02 An out-of-date server shows the banner and "Unknown" for Needs a look.
  - [x] 001.05 A date appears only for `capacityOutlook.status = estimated`.
  - [x] 001.06 With dataset reads off, the disclosure explains how to turn them on and shows no numbers.
  - [x] 001.08 Enclosure/slot is hidden when `disk.query` doesn't report it.
  - [x] 001.09 The status rules table is implemented in order, with unit tests for each row.
  - [x] 001.11 The raw model is visible wherever a nickname or suggested name is shown in the panel.
  - [x] 001.12 "Confirmed" disappears if the drive's identity changes after confirmation.
  - [x] 001.13 The lifespan bar follows `DriveAgeReviewYears` and always shows the "not a failure prediction" caption.
  - [ ] 001.17 The wrapper refuses every command except the two allowed ones (tested), and sleeping drives are never woken.
  - [x] 001.19 Counter resets never draw as decreases.
  - [x] `/storage?disk=<id>` deep links still work, and old deep links don't 404.

## Implementation notes

Implementation is in the working tree. The package remains **in progress** pending merge; automated verification is recorded below. The SMART flag remains off by default, including on the running development instance. No remote SSH account, permissions or keys were installed.

### 001.01

`GET /api/v1/storage/overview` returns `servers`, each containing only its own pools and drives. `StorageView`, `storageModel` and `Storage.css` replace the former multi-server topology/evidence entry point. Selection uses `server` and `disk` query parameters; a disk deep link selects its owning server. Batched server reads avoid per-drive database requests. The summary says “pool needs a look” or “partly unknown” when warranted, rather than claiming all drives are healthy in a degraded/unknown installation. Browser checks cover switching and deep links.

### 001.02

The same endpoint supplies `freshness`, `lastSuccessUtc`, `poolCount`, `driveCount`, nullable `rawCapacityBytes` and `attentionCount`. Freshness uses three poll intervals with a five-minute minimum. Paused/no collection remain explicit. Raw capacity is unknown if any disk size is unknown. Attention is also unknown when no known drive issue can be counted but inventory/pool coverage or a physical member match is incomplete; this deliberately avoids a misleading zero. Loading, empty, first failure, retained session data, timeout and malformed nested responses are browser tested. The shell subtitle now uses the package copy.

### 001.03

Pool objects return `guid`, `name`, semantic `status`, `layout`, `memberCount`, nullable `toleratedFailures`, `usedBytes` and `sizeBytes`. Redundancy uses the existing conservative assessment; nested replacement trees remain “Layout unknown” rather than inventing a failure allowance. Stale pool collection cannot produce a current healthy pill. `StorageBar` has a text alternative. Responsive table/card layouts are checked at 1440, 900 and 390 pixels.

### 001.04

Each pool includes the latest `scan` snapshot; `StorageView` renders completed scrub/resilver, running, paused and missing scan states. Missing progress/error values remain unknown. Stale server scan copy appends “(last known)”. Browser fixtures cover completed scrub and running scrub; the running app also renders its real resilver.

### 001.05

`capacityOutlook` reuses `CapacityOutlook.ReadAsync` and its guarded statuses. Only `estimated` with a projection gets a date. Stale/paused server forecasts are paused. Browser checks cover projected dates and stale suppression; existing capacity tests retain the domain coverage.

### 001.06

Root-only response fix: keep the root dataset when it is the only dataset returned for a pool, and explain the absence of a folder breakdown. When children are returned, keep omitting the cumulative root to avoid double counting. Successful empty lists now have an explicit message. This fixes the real Media NAS response containing only Main. Regression coverage includes root-only/mixed/missing datasets and the rendered root usage.

Pools include `datasets` and `datasetStatus`. `StorageView` shows the top six and sums remaining values as Other, keeping unknown sizes explicit. Disabled collection shows the Settings explanation without numbers; unavailable retained datasets are labelled last known. The disclosure precedes drive buttons in keyboard order while CSS keeps the mock's placement below the table. Browser checks cover disclosure, Other and disabled reads.

### 001.07

Each server's `alerts` contains only its active, non-dismissed NAS alerts. The header chip is absent when none exist. The disclosure shows source class, level, message and last-seen time. Browser fixtures verify expansion and server isolation.

### 001.08

Owner-requested grouping: each drive now includes nullable `group: { id, name, role }` from its top-level ZFS vdev. Parent traversal keeps nested replacement/spare trees inside the original group; cyclic or missing ancestry remains unknown. Storage renders contiguous, naturally ordered vdev row groups with headings and alternating token-based shading, also used on mobile cards. Unassigned drives remain in their existing card. No new NAS calls or stored-data changes are needed. Unit/integration tests cover ancestry, unknowns and API projection; browser checks cover grouping, striping and light/dark accessibility. This intentionally extends the mock's flat drive list.

Drive data contains the retained `disk` record plus pool association, status, acquisition and plan. Collector enrichment reads size/type/rotation/enclosure/slot from the existing `disk.query` response; no extra RPC was added. Inventory values remain nullable. Drive rows live in their pool card, with unassigned records under Not in a pool. Stable IDs from stored mappings are preferred; exact unique source-reported disk aliases may be matched within the inventory freshness window, without guessing partition suffixes. Unmatched ZFS objects are counted and explained explicitly. Mobile cards, null enclosure/slot, and distinct servers are covered by browser/backend checks.

### 001.09

`StorageStatusRules` implements ordered missing, critical, watch, SMART-unknown, spare and healthy rules, with stale wrapping. Open findings and bounded, token-matched current NAS alerts participate. Missing ZFS state/counters and stale source capabilities remain Unknown instead of falling through to Healthy. Pool status preserves degraded/unknown states. Unit cases cover each priority row, SMART gating, resolved findings and stale precedence.

### 001.10

Temperature availability clarification: the API returns `temperatureNote` to distinguish disabled collection, an empty successful TrueNAS cache, stale/unavailable reads and unmatched drive readings. Health displays Collection disabled or No cached reading with the explanation rather than an unexplained Unknown. Unit and light/dark browser checks cover the empty-cache case. No NAS read arguments changed.

Owner-requested footer refinement: actions use equal-width columns on desktop/tablet and full-width stacked rows on phones. A sole action or third SMART import action spans the footer, avoiding an empty half-row. This deliberately replaces the mock's loosely sized flex buttons.

Follow-up: scoped the app header/footer rules to the shell, so their fixed heights and text styles no longer affect the drawer. The drawer header does not shrink. A layout regression checks long drive names and reported models stay inside the header and above Health at 1440, 900 and 390 pixels.

`StorageDrivePanel` replaces the per-drive evidence screen. Radix supplies the desktop/tablet modal trap; heading focus, Escape, explicit close and focus return are tested. Phones render a page with a server/pool back link and hide the duplicate page heading. Health, Where it is, Cost & lifespan and History form the four groups. Old evidence components remain in the repository but are unlinked from Storage. Their obsolete navigation checks were removed from `workflows.spec.ts`; the new experience is covered by `storage.spec.ts`, and retained backend endpoints keep their domain/API tests.

### 001.11

Nullable `Disk.Nickname` and `PUT /api/v1/storage/drives/{id}/nickname` save a name independently, without rewriting cost/date evidence. Suggested names use conservative model prefixes (including WDC's underscore variant); the raw model is always shown in the panel. Name validation, persistence and raw-model rendering are checked through real API and browser tests.

### 001.12

`PUT /api/v1/storage/drives/{id}/location` requires the existing exact serial, unambiguous identity, bay label and physical-check evidence. `BayConfirmedIdentity` binds confirmation to identity key/model/serial/WWN. Existing confirmations without that binding are not silently renewed. Existing metadata saves also record the binding. Unit/API tests verify identity drift and rejection of a wrong serial; browser checks verify the resulting confirmation.

### 001.13

`PUT /api/v1/storage/drives/{id}/profile` atomically saves acquisition plus purchase/warranty/notes, requiring the acquisition revision. The estimate checkbox maps to purchase/installation confidence. Existing non-USD acquisition records retain their currency and label rather than being silently converted. The service plan uses `DriveAgeReviewYears`; the caption explicitly disclaims a failure prediction. Drafts survive rejection, and successful saves announce Saved. Real API checks cover validation, stale-revision conflict, persistence and nickname independence; browser checks cover editing and estimated dates.

### 001.14

`GET /api/v1/storage/drives/{id}/history` merges discovery, acquisition dates, non-voided maintenance/replacements and finding opened/resolved events, newest first, bounded to 100 items. `POST /api/v1/storage/drives/{id}/notes` creates an operator maintenance note with an idempotency key and no asserted hardware action. Real API tests check retry deduplication and history; the panel browser test adds a note.

### 001.15

The overview includes the first open plan for the drive; complete plans are excluded. The panel's form saves through existing `POST /api/v1/plans`, prefilled with the disk/title. Open plan navigates to `/planning?plan={id}`; App focuses and scrolls that plan row. The replacement action is absent when a plan exists. Browser checks cover creating and linking the plan.

### 001.16

The panel posts completed replacement details through existing `/replacements`, prefilled with the old drive, with same-server candidate selection and both identity confirmations. The new drive's acquisition install date and revision are updated in the same save as the replacement, preserving its cost fields. The real API test verifies the new installation time; the panel browser check submits both identity confirmations, and existing replacement domain tests remain in place. No NAS replacement operation is performed.

001.16 follow-up (2026-11-04): save errors appear inside the form beside its actions and receive focus, preserving the draft. Successful saves also receive focus so feedback scrolls into view. Serial confirmations trim surrounding whitespace. Browser coverage exercises rejection and retry; the real API test verifies replacement history and installation-date persistence through subsequent reads.

### 001.17

`SmartSshCollector` plus an injectable `ISmartSshTransport` runs fixed commands with strict host-key validation, bounded output/time and disabled forwarding/PTY. Per-server configuration accepts 6–12 hour intervals, including a reachable externally managed tunnel endpoint. `scripts/smart-read-wrapper.py` enforces the exact two commands and a fresh-scan device check on the NAS. `SmartRead` retains last successful curated data plus skip/error state. Command grammar and standby retention have .NET/Python tests. Setup, limited sudo, 24.10 middleware source inspection, UI fallback and activation limitations are documented in `docs/SMART_SSH_SETUP.md`, the NAS read manifest and README. The flag is **off**, and no live SSH setup/power-state validation is claimed.

### 001.18

When `Homelab:SmartEnabled` is enabled in a validated deployment, the overview returns nullable `smart` details from the latest curated successful SSH/imported report. `StorageHealth` renders read age, skipped sleep, last-known errors, counters, wear, firmware and reported self-test result. Source self-test clock time is unavailable from the existing ATA log contract and is labelled unknown rather than inferred from polling/lifetime. With no report, copy says “No SMART reading is available yet” rather than claiming unsupported hardware without evidence. All SMART tiles and firmware are absent with the flag off. Browser checks cover flag off, missing report and asleep states.

### 001.19

Flagged `GET /api/v1/storage/drives/{id}/trends` returns changed counters over eight weeks, bounded to 10,000 readings. Lines break at unknown/different epochs, source/schema/unit changes, duplicated times and counter decreases. Isolated points preserve evidence without inventing continuity. `StorageHealth` draws text-labelled SVG segments. Unit tests verify reset/unknown boundaries; browser checks render a reset-separated trend in both themes. SSH reads do not manufacture an epoch, so those readings remain isolated unless future evidence establishes continuity.

### 001.20

Import SMART report is only mounted with the flag on. It opens the existing `SmartReportImport` dialog directly above the drive panel; browser checks cover the nested dialog in both themes and assert flag-off absence. Existing import endpoints, validation and report provenance remain unchanged.

### Verification and remaining review

- Backend: all 792 tests passed in the latest full run, including the real authenticated storage API write/read checks.
- Python wrapper: all 3 command/refusal cases passed.
- Browser: all 8 workflows passed in the latest combined run (7 storage workflows plus the wider application workflow). Light/dark axe checks cover normal, stale, loading, empty, unavailable, mobile panel, SMART variants and the import dialog. All final builds passed; lint exits successfully with existing warnings.
- Visual mock comparison: used the owner-provided gallery at `http://127.0.0.1:4178/001-storage-redesign/mock.html`. Stepped through all six states, all three widths, both themes and both SMART settings, plus desktop/mobile panels. Compared implementation captures and adjusted pool-header spacing, loading shapes and date presentation. Mocks, fixtures and design copy were not modified.

## Change log

| Date | Change | By |
| --- | --- | --- |
| 2026-11-01 | Package created at lock-in. Loading, empty and unavailable states, the Act now / Missing statuses, and the status rules were written down at lock-in, following the canvas's existing patterns. | Claude |

### 001.18 — API power-on hours follow-up (2026-11-01)

Added owner-requested optional `ReadApiPowerOnHours` setting, off by default, independent of the SSH SMART flag. A typed, tightly bounded `disk.smart_attributes` read runs every six hours for up to 64 inventory devices, with a second inventory rejecting changed identities. The setting explains that active API reads can wake drives. The endpoint only supports ATA; NVMe is skipped, SAS/other ATA-only rejections display “Not supported via API” in the Powered on card. Permission denied, absent/ambiguous hour counters, collection disabled, and stale readings have separate explanations. Only explicitly labelled safe-integer ATA hour counters are retained; no new health conclusions or historical observations are inferred.

`/api/v1/storage/overview` adds `drives[].apiPowerOnHours: {status,hours,fetchedUtc,detail}`. The setting and a separate connection-scoped snapshot table are migrated locally. Components: `StorageHealth`, `OperationsSettings`, `storageModel`; adapter: `ApiPowerOnHours`, typed NAS client/session method, collector. This extends the original SSH-only data proposal at the owner's request. Unit/integration tests cover opt-in reads, six-hour throttling, unsupported types, denials, changed identity, units and RPC boundaries. Browser coverage includes every card state with light/dark axe checks. Live NAS permissions and real payloads still require the owner to enable the setting.

### 001.18 — Run API power-on hours now (2026-11-01)

Added “Run now” beside the API power-on hours option. It requires the option to be saved as enabled, runs a collection cycle immediately on enabled NAS connections, bypasses the six-hour SMART interval, and reports per-server counts of available hours, unsupported drives, denied reads and unavailable counters. `POST /api/v1/storage/power-on-hours/run` is authenticated and requires the normal same-origin request header. The collector owns its short write leases around network work; concurrent manual requests are rejected, and no disabled connection or unsaved setting is activated. Tests cover forced reads despite a fresh snapshot, disabled-setting rejection, and UI running, completion and retry/error states.

001.18 follow-up: the Powered on card now shows approximate accumulated years/months immediately below the numeric hours, using a 365.25-day year and twelve equal months. Applies to API and SSH values, including last-known values; absent counters retain their explicit state without a duration. This represents powered-on time, not calendar age. Existing card-state browser coverage checks the subtext.

001.18 presentation follow-up: removed the collection-source/fetch caption beneath populated Powered on cards at the owner’s request. Numeric hours, approximate years/months and last-known labels remain; missing/unsupported states retain their explanations.

### 001 — Complete inventory CSV export (2026-11-02)

Owner-requested Export CSV button sits beside Refresh in the Storage page heading. Authenticated `GET /api/v1/storage/export.csv` exports all servers and all retained inventory/evidence/history in a consistent SQLite read transaction, independent of server selection, pagination or collection flags. The single versioned UTF-8 CSV includes readable columns plus full typed `record_json` records, stable IDs and a final schema/migration/count/checksum manifest. Version 2 is restricted to storage tables, drive-linked plans and shared asset records directly referenced by storage reviews. Unrelated plans/assets, hardware-page records, recovery, global settings and activity are omitted. Owner accounts, notification channels/queues, backup-file records and integration credentials are excluded. No NAS calls occur.

The offline `scripts/restore-storage-csv.py` validates the CSV against a trusted same-version schema, restores to a new directory only, checks relationships and integrity, and leaves restored NAS connections disabled. Other application sections start empty. It restores application inventory, not NAS configuration or files. [Format and restore instructions](../../STORAGE_CSV_EXPORT.md). This intentionally extends the design package at the owner's request; no mocks or fixtures were changed. Tests perform a real CSV-to-SQLite restoration and compare every exported record/column except deliberate safety changes; they also cover credentials, Unicode/quotes/multiline notes, formula-safe readable cells, modified exports and overwrite refusal. HTTP tests verify authentication and attachment headers. Browser checks cover all-server downloads from a selected server, running/error/retry states, mobile layout and light/dark axe checks.

Storage CSV scope correction (2026-11-02): the owner clarified that “all” means all storage information. Replaced the broad table exclusion approach with a fixed storage-table allowlist and filtered supporting references. The restore format is now version 2 with explicit `scope: storage`. Mixed-module regression fixtures verify unrelated records are absent and storage relationships still restore.

### 001.09 — Specific Watch alert explanations (2026-11-02)

Owner-reported generic Watch explanations now show the actual active, non-dismissed TrueNAS alert text for the matched drive through the existing `status.why` contract. All matching messages are retained and deduplicated, including reported pending and offline-uncorrectable sector counts. Messages are rendered as plain text; counters are not summed, converted into reallocated sectors, or promoted into SMART observations or overall pass/fail claims. Missing SMART data stays unknown. Serial matches require uniqueness; device-name matches require fresh inventory, a present unambiguous drive, and a unique alias. Token boundaries reject partition names/partial serial matches. Retained alerts from denied/stale alert reads are labelled last known. Existing critical/missing/finding precedence remains unchanged. No additional NAS reads or status-severity changes were introduced. Unit/integration coverage checks exact active counts, dismissal/absence, wrong-drive/NAS matches, stale/ambiguous aliases, plain text and evidence separation. Browser coverage checks detailed Watch copy, mobile layout and light/dark axe.

001.06 follow-up (2026-11-04): active fresh resilvers breathe with a three-second opacity animation, disabled under reduced motion. Scan text displays the existing TrueNAS `scanRemainingSeconds` (`total_secs_left`) estimate as approximate minutes/hours/days. Missing, zero, paused and stale estimates are withheld; no estimate is inferred from rounded progress. Browser coverage checks the source estimate, reduced motion, paused and stale states, and absent timing.

001.09/001.10 follow-up (2026-11-04): alias-only NAS alerts are no longer assigned to a drive if another inventoried physical drive has used that alias, including absent drives. Alerts remain visible at server level, and serial-matched drive alerts retain precedence. Fresh active RESILVER evidence with a stable mapped ONLINE disk beneath REPLACING and an unavailable sibling yields Resilvering status. Genuine Watch/Act now issues remain primary with replacement context added. Paused, completed or stale scans cannot yield a current Resilvering status. API tests cover recycled aliases, serial-specific issues, completion and stale snapshots.

001.16 follow-up (2026-11-04): absent drives with a non-voided replacement record are omitted from active Storage pool/loose-drive listings and drive/attention/capacity counts. Original inventory, history, exports and alias ownership records remain intact. Voiding the replacement restores visibility; a drive reported present remains visible even with a replacement record. API tests cover hiding, record retention, voiding and present-drive visibility.
