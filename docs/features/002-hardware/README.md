# 002 · Hardware

- **Status:** ready · 2026-11-01
- **Mock:** [mock.html](mock.html). Use the toolbar to switch State, Width, Theme and the `hwmetrics` and `synology` flags.
- **Shared preview:** https://claude.ai/artifact/Efch25rsx3RhY8akxSaMCc (the owner's design canvas, private unless shared)
- **Affected screens:** a new **Hardware** page (`/hardware`, including `/hardware?server=<id>&component=<id>`), placed in the navigation next to Storage.

## Goal

For each server, the owner can see how hard the hardware is working, and catch two things early: **signs that a part is starting to fail**, and **signs that it's undersized and needs an upgrade**. The page also keeps a record of what's inside each box: what was detected, what the owner entered, and what it cost. The platforms are TrueNAS and Synology only.

## Design summary

- **One server at a time,** with the same server tabs as Storage. Servers are never mixed on one screen.
- **Two sections: "How it's running" and "Components".**
  - "How it's running" has four metric cards: CPU use, memory, temperature and power draw. A 24 h / 7 / 30 / 90 day switch sits above them, and a coverage line says exactly how many samples the numbers are based on.
  - "Components" is the inventory, grouped as Compute, Power & cooling, and Expansion & chassis. Each component opens a side panel, in the same pattern as Storage's drive panel.
- **Two statuses, never merged:**
  - **Watch** (amber) means signs of trouble.
  - **Near its limit** (blue) means sustained use above the owner's review level: the part is undersized, not faulty.
  - A busy CPU should never read as a broken one.
- **There is no replacement cycle and no lifespan bar.** The owner said utilization and failure signs are what matter. Purchase details stay, as a plain record.
- **Every value says where it came from:**
  - "Detected" (blue text) or "You entered" in the inventory.
  - "Collected" samples are drawn as lines; readings you entered are drawn as dots and captioned "entered by you".
  - Unsupported metrics show a hatched "Not measured" placeholder. **The owner will design that treatment later.** Treat it as a placeholder, not final copy.
- **Memory is split into apps / cache / free.** On TrueNAS the cache is ZFS's ARC, labelled "ZFS cache"; on Synology it's "File cache". Only apps count as used, otherwise every TrueNAS box looks full.
- **Labels follow the platform:** "CPU temperature" on TrueNAS, "System temperature" on Synology.
- **Automatic and manual collection mix freely.** Any server can take entered readings: power draw is never collected on either platform. A Synology that's enrolled but not connected runs entirely on entered readings and entered parts.

Rejected alternatives:
- **Replacement-plan years per component** were explicitly dropped by the owner.
- **Proxmox, generic Linux and BMC/Redfish sources** are out of scope for the foreseeable future.
- **Servers added by hand with no platform** (002.03) were dropped: only TrueNAS and Synology servers are enrolled.
- **A single "used memory" figure** was rejected because it misleads on ZFS and DSM, where cache fills spare memory.

## Features

| ID | Feature | Depends on | Flag | Status |
| --- | --- | --- | --- | --- |
| 002.01 | Server switcher (tabs with status dot and summary line) | — | — | ready |
| 002.02 | Server header: connection mode, freshness, 4 facts; out-of-date, not-connected, collecting and unavailable banners | 002.01 | — | ready |
| 002.03 | Servers added by hand with no platform | — | — | dropped |
| 002.04 | Metric cards: CPU use, memory, temperature, power draw | 002.02 | — | ready |
| 002.05 | Time range switch (24 h / 7 / 30 / 90 days) and sample coverage line | 002.04 | — | ready |
| 002.06 | Memory split: apps / cache / free, with platform-specific cache label | 002.04 | — | ready |
| 002.07 | Review levels: CPU %, memory %, temperature °C, editable in Settings; dashed line on charts; "above on n days" lines | 002.04 | — | ready |
| 002.08 | TrueNAS metrics collector: CPU, memory split, CPU temperature from TrueNAS reporting (backend) | — | `hwmetrics` | ready |
| 002.09 | Entered readings: "Add reading" form; readings drawn as dots | 002.04 | — | ready |
| 002.10 | Component inventory grouped by kind (table on desktop, cards on phones) | 002.01 | — | ready |
| 002.11 | Detected vs entered sources on every value; detected/entered mismatch | 002.10 | — | ready |
| 002.12 | Component status rules (below) | 002.10 | — | ready |
| 002.13 | Component panel: why box, How it's running, Details, Purchase, History, action bar | 002.10 | — | ready |
| 002.14 | Memory slot map with upgrade room | 002.13 | — | ready |
| 002.15 | Add / edit component (entered parts) | 002.10 | — | ready |
| 002.16 | "Plan an upgrade" and "Mark as replaced" | 002.13 | — | ready |
| 002.17 | Synology support: enrolment, connection, inventory and metrics collector (backend + minimal setup) | — | `synology` | ready |
| 002.18 | Inventory from reads the app already makes (`system.info`): CPU model, cores, total memory, board, OS version, uptime | 002.10 | — | ready |

`hwmetrics` and `synology` follow the usual rule: with a flag off, everything it powers is absent, with no placeholders. With `hwmetrics` off, TrueNAS servers still show inventory (002.18), uptime and entered readings. CPU, memory and temperature then come only from readings, or show the unsupported placeholder. With `synology` off, Synology servers don't appear at all.

## States

The mock's **State** switcher covers each row. Server tabs, the range switch and component names also work inside the mock.

| State | When it happens | What the user sees | Fixture |
| --- | --- | --- | --- |
| Normal | TrueNAS server reporting, metrics collected | media-nas: CPU on **Watch** for heat, everything else fine, power "Not measured" | `fixtures/server-truenas.json` |
| Near its limit | Sustained use above the review level | ds920 (Synology): CPU **Near its limit**; 30-day coverage "18 of 30 days"; power from 2 entered readings | `fixtures/server-synology.json` |
| Not connected | Enrolled Synology with no working connection | Info banner; "Entered by hand"; metrics from readings (dots); all values "You entered"; collection-only statuses fall back to Not monitored | `fixtures/server-synology-not-connected.json` |
| Out of date | Last successful collection is older than the freshness window | Amber banner; values captioned "last known"; statuses prefixed "Last known"; Needs a look "Unknown" | `fixtures/server-backup-stale.json` |
| Collecting | Collection started recently, without enough history for the selected range | Info banner; cards show the latest sample and "not enough history for a 30-day average yet"; partial chart line | — (`coverage.samples` small) |
| Component panel | Owner opens a component | Side panel (desktop/tablet) or full page with "← server · Components" (phone) | `fixtures/component-nas-cpu.json` |
| Add a reading | "Add reading" from the header or a card | Modal form (desktop/tablet) or full page (phone) | `fixtures/new-reading.json` |
| Add a component | "Add component" from the header | Modal form (desktop/tablet) or full page (phone) | `fixtures/new-component.json` |
| Empty | No servers enrolled | "No servers yet" with a link to Settings | `fixtures/servers-empty.json` |
| Loading | First load, nothing cached | Skeleton tabs, facts, 4 cards and the component card | — |
| Unavailable | Hardware request failed | Error banner with Retry; data loaded earlier in the session stays visible, labelled | — |
| `hwmetrics` off | TrueNAS collector not shipped | TrueNAS cards show readings or the placeholder; CPU/memory components show Not monitored unless readings exist | same fixtures, collected fields absent |
| `synology` off | Synology support not shipped | No Synology tab or server, and the Settings copy mentions TrueNAS only | `fixtures/servers.json` minus Synology rows |
| Mobile (≤ 700px) | Narrow viewport | Tabs scroll sideways; facts 2×2; header buttons 2-up; range switch full width; cards stack; component table becomes cards; panel and dialogs become full pages | — |

## Copy

Use these strings as written. `{…}` marks data.

- Page: title "Hardware", subtitle "Components and how they’re running, one server at a time."
- Tab summary line:
  - "{n} components · nothing to look at"
  - "{n} components · {k} to watch", "· {k} near its limit", or both, joined with " · "
  - "Out of date · {relative}"
  - "Entered by hand"
- Server header: "{host} · {platform and version} · last collected {relative}". For not-connected servers: "last reading entered {relative}".
  - Freshness pill: "Reporting", "Out of date" or "Not connected".
  - Mode pill: "Collected automatically" or "Entered by hand".
  - Buttons: "Add reading", "Add component".
- Facts, when collected: "CPU · {range}-day avg" (sub "Peak {p95}%"), "Memory used by apps" (sub "of {total} GB"), "Uptime" (sub "Since {date}"), "Needs a look" (sub "Components"). When out of date, subs start "Last known · " and Needs a look reads "Unknown".
- Facts, readings only: "CPU · avg of readings", "Memory · avg of readings", "Readings" ("{n}", sub "In the last {range}") on not-connected servers, or "Uptime" on connected TrueNAS servers with `hwmetrics` off.
- Banners:
  - Out of date: "**Can’t reach this server right now.** Everything below is from the last successful check, {relative}. Averages stop at that point, and component status may have changed since."
  - Not connected: "**This server isn’t connected, so its readings are entered by hand.** Averages come only from the readings you add and can miss busy or quiet periods. Add a reading or connect it in Settings to collect automatically."
  - Collecting: "**Collecting since {relative}.** Each average appears once its whole range has been sampled: 24 hours tomorrow, 30 days in a month. Until then, cards show the latest sample."
  - Unavailable: "**Hardware couldn’t be loaded.** {error}. Showing what was loaded {relative}." or "Nothing has been loaded yet.", then "Retry".
- Coverage line, under "How it’s running":
  - "Based on {samples} of {expected} five-minute samples." Add " ({pct}%)" when below 100%. Add " One gap: {duration} on {date}." or " {n} gaps." when gaps exist.
  - Partial history: "Covers {d} of {range} days: collection started {date}."
  - Downsampled: "Older than 30 days is kept as hourly averages."
  - Synology: "DSM keeps no history the app can read, so the app records its own." Show this only if that turns out to be true.
  - Readings: "From {n} reading(s) you entered. A few spot readings can miss busy and quiet periods.", or "No readings in the last {range}."
  - Out of date: "Last known: samples up to {date, time}."
- Card captions:
  - "{range}-day average"
  - "{range}-day average · last known, up to {date}"
  - "Latest sample · not enough history for a {range}-day average yet"
  - "From {n} readings · entered by you"
- Card lines:
  - "Peak {p95}% (95th percentile)"
  - "Previous {range}: {value}". Hide this line when the previous period wasn't sampled.
  - "Never above your {level}% review level"
  - "Above your {level}% review level for an hour or more on {n} of the last {days} days"
  - "Hottest {max} °C"
  - "Above your {level} °C review level on {n} of the last {days} days"
  - "Not above your {level} °C review level today / in the last {range}"
  - Readings: "Highest reading {v}", "Above your {level}% review level in {k} of {n} readings"
  - Power: "Last reading {date}: {w} W", "About {kWh} kWh a month at this rate"
- Memory legend: "Apps {x} GB · {ZFS cache|File cache} {y} GB · Free {z} GB", then the note:
  - TrueNAS: "ZFS borrows spare memory as a disk cache and gives it back when apps need it, so it isn’t counted as used."
  - Synology: "DSM uses spare memory to cache files and gives it back when apps need it, so it isn’t counted as used."
- Unsupported placeholder (owner will redesign): "Not measured", plus the platform's reason, e.g. "TrueNAS doesn’t report power draw. Add a reading from a power meter or your UPS to track it here.", and the link "Add a reading". Readings-only variants: "No readings", "Not in your readings".
- Review levels line: "Review levels: CPU {a}%, memory {b}%, temperature {c} °C. Dashed lines on the charts. Change in Settings"
- Components:
  - Summary: "{n} components · {d} detected, {e} entered by you", plus " · {k} to watch" and/or " · {k} near its limit".
  - Groups: "Compute", "Power & cooling", "Expansion & chassis".
  - Table headers: Status, Component, Details, Source, In service.
  - Sources: "Detected", "You entered", "Detected + entered".
- Panel:
  - Eyebrow "{kind} · {server}".
  - Why box: "**Why “{status}”:** {sentences}". Show it for Watch, Act now and Near its limit only.
  - Sections: "How it’s running", "Memory slots", "Details", "Purchase", "History".
  - Chart caption: "{metric}, last {range}. The dashed line is your review level." For readings: "{metric}, your readings. The dashed line is your review level."
  - Not-monitored notes are per kind, e.g. "Power supplies don’t report to TrueNAS…", "Fan speeds aren’t collected. CPU temperature is the best sign of how cooling is doing.", "Synology reports whether each fan is working or has failed. It doesn’t report speeds."
  - Mismatch line: "The detected total matches the modules you entered." or "Detected {x}, but you entered {y}. A module may have failed or come loose."
  - Purchase: "Price paid" ("${cost} · about ${per year} per year so far" or "Not recorded"), "Installed", "Warranty".
  - Actions: "Edit details", "Plan an upgrade", "Mark as replaced". "Add note" under History.
- Slots: "{used} of {total} slots used · {installed} installed · board supports up to {max}", or for soldered memory "{installed} installed: {soldered} soldered to the board, plus {n} module(s)". Then one plain planning line:
  - "Room for {n} more modules. To keep dual-channel, add them as a matching pair."
  - "No free slots. Reaching {max} means {n} × {size} modules."
  - "Synology lists {max} as this model’s maximum, so it’s already at the supported limit."
- Add reading:
  - Title "Add a reading for {server}", intro "Copy these from the server’s own dashboard, a power meter or your UPS. Leave blank anything you didn’t measure."
  - Fields: "Taken", "What it shows" ("A moment in time" / "An average over a period"), "CPU use" (%), "Memory used" (GB of {total} GB), "Temperature" (°C), "Power draw" (W), "Note".
  - Footnote: "Readings show as dots on the charts, apart from collected samples, and are labelled “entered by you” wherever they’re used."
  - Buttons: "Cancel", "Save reading".
- Add component:
  - Title "Add a component to {server}", intro "For parts the app can’t detect, like power supplies, coolers and cards. Detected parts appear on their own."
  - Fields: Kind, Name, Model or part number, Key specs, Price paid (USD), Purchase date, Install date, Warranty ends, "These dates are estimates", Notes.
  - Buttons: "Cancel", "Add component".
- Empty: heading "No servers yet", body "Connect a TrueNAS or Synology server in Settings and its processor, memory and board appear here.", button "Go to Settings". Drop "or Synology" while `synology` is off.

## Component status rules (002.12)

Evaluate top to bottom; the first match wins. Component status always uses **the last 30 days**, whatever range is selected. Card pills follow the selected range (see below).

| Status | Tone | Rule |
| --- | --- | --- |
| Last known: {status} | neutral | Server is out of date: show the last computed status, prefixed |
| Act now | bad | A detected part is missing or failed: detected memory total is below the entered total, or the platform reports a fan as failed |
| Watch | warn | Temperature above the review level on at least 1 day in the last 30 (a CPU, or the Synology unit's system sensor) |
| Near its limit | info | CPU use, or memory used by apps, above the review level for at least `sustainedMinutes` (60) in a day, on at least 3 of the last 30 days. For readings only: at least 2 readings in the last 30 days above the level, and at least half of them |
| Healthy | ok | The component has metric data or a detected state (e.g. fan OK), and nothing above applies |
| Not monitored | neutral | Nothing measures this component (PSU, case, cards, a board), or its metric has no data in the last 30 days |

- **Card pills:** "Watch" or "Near its limit" appears on a card when the same rule is met within the selected range. The 24 h and 7-day ranges need at least 1 day; the 30 and 90-day ranges need at least 3 days (1 for temperature). Out-of-date servers show "Last known: {pill}" in neutral.
- **Why text** states what was observed and nothing more. Use plain advice only where it follows directly, e.g. check the cooler. Never predict failure dates or probabilities. For Near its limit, say explicitly that nothing points to a fault.
- **Which component a metric belongs to:** CPU use and CPU temperature go to the processor component. Memory goes to the memory component. On Synology, the system temperature goes to the NAS unit; if Codex finds the API reports a CPU temperature, it goes to the processor instead.

## Data needs

Fixtures show a proposed shape. Codex owns the real contract, and may for example serve one `/hardware/servers/{id}?range=30d` call plus a lighter tab list.

| Feature | Need | Proposed field(s) | Notes |
| --- | --- | --- | --- |
| 002.01 | Server list | `id, name, host, platform, connection, freshness, lastSuccessUtc, componentCount, watchCount, nearLimitCount` | Same servers and freshness window as Storage. Counts are null when out of date |
| 002.02 | Header and facts | `platformVersion, uptimeSeconds, bootedUtc, lastReadingUtc` | Uptime from `system.info` on TrueNAS (already read) |
| 002.04–05 | Metrics for a range | `metrics.range`, `metrics.coverage { intervalSeconds, expectedSamples, samples, firstSampleUtc, collectionStartedUtc, downsampledToSeconds, gaps[] }`, then `cpu`, `memory`, `temperature`, `power`, each with `source: collected / entered / none` | Return one series point per chart bucket (about 72–300 points), never raw samples. A gap is ≥ 3 missing intervals. Keep 5-minute samples for 30 days and hourly averages for at least 1 year |
| 002.04 | CPU | `averagePercent, p95Percent, previousAveragePercent, daysAboveLevel, latestPercent, series` | `previous*` is null when the previous period wasn't fully sampled |
| 002.06 | Memory | `totalBytes, appsAverageBytes, appsPeakBytes, previousAppsAverageBytes, cacheKind (zfs-arc / page-cache), cacheAverageBytes, freeAverageBytes, daysAboveLevel, series (apps bytes)` | "Apps" = total − free − cache. Codex confirms the exact derivation per platform |
| 002.04 | Temperature | `sensor (cpu / system), averageC, maxC, maxAtUtc, daysAboveLevel, series` | Whichever sensor the platform reports; the label follows `sensor` |
| 002.04, 002.09 | Power | `source, averageWatts, readings[{ id, atUtc, watts }]` | Never collected on either platform |
| 002.07 | Review levels | `reviewLevels { cpuPercent, memoryPercent, temperatureC, sustainedMinutes }` | Defaults 80 / 80 / 80 / 60. Global, editable in Settings, alongside the existing operating settings |
| 002.08 | TrueNAS collection | TrueNAS reporting data for CPU, memory (with ARC) and CPU temperature | **New read methods.** Verify names and arguments against the pinned 24.10 middleware commit, as for `disk.temperatures`, and add them to `docs/NAS_READ_MANIFEST.md`. Prefer backfilling from TrueNAS's own history on first connect, which makes the Collecting state short or unnecessary |
| 002.09 | Readings | `POST` body as in `fixtures/new-reading.json`; list, edit and delete | At least one measurement required. Readings never mix into collected averages: when both exist for a metric in a range, show collected data and keep readings as dots |
| 002.10–11 | Components | `id, group, kind, name, spec, source (detected / entered / detected+entered), installedUtc, status, why, metric, details[{ label, value, source }]` | Detected values come from collection and can't be edited, only annotated. Entered values are the owner's |
| 002.11 | Mismatch | `detectedMismatch { field, detected, entered }` | E.g. detected memory total ≠ sum of entered modules. Feeds the Act now rule |
| 002.13 | Panel | `purchase { priceCents, currency, purchasedUtc, installedUtc, warrantyEndUtc, datesEstimated }`, `history[{ atUtc, kind, text }]` | History merges: installed, entered notes, detected changes (BIOS, DSM version, core count, memory total) and status changes |
| 002.14 | Slots | `slots[{ name, sizeBytes }], soldered[{ sizeBytes }], maxSupportedBytes, maxSupportedSource, detectedTotalBytes, enteredTotalBytes` | Slots and the maximum are usually entered; nothing we read reports DIMM slots |
| 002.15 | Add / edit component | body as in `fixtures/new-component.json` | Kinds: processor, memory, motherboard, power-supply, cooling, storage-controller, network-card, graphics-card, case, nas-unit, other |
| 002.16 | Upgrade / replace | "Plan an upgrade" opens the existing Planning form prefilled with the component; "Mark as replaced" retires the component, with its history, and starts its successor | Reuse Planning; don't build a second plan store |
| 002.17 | Synology | Enrolment and connection, inventory (model, CPU, memory total, DSM version, fan states), CPU / memory / system temperature samples | **The access method is Codex's call** (DSM Web API, SNMP, or both). Keep it read-only and document the boundary like the NAS read manifest. The app stores its own samples if DSM exposes no history. Settings for enrolment can be minimal while the flag is off |
| 002.18 | Inventory from current reads | CPU model, core counts, physical memory, board / system product, OS version, uptime from `system.info` | No new access. Use whatever fields the pinned contract provides and hide anything it doesn't |

## Interaction and accessibility

- **Keyboard path:** server tabs (buttons with `aria-pressed`), then header buttons, then range buttons (`role="group"` labelled "Time range", `aria-pressed`), then card links, then component name buttons in table order. A component name opens the panel and moves focus to the panel heading. Esc or Close returns focus to the button that opened it. Dialogs work the same way.
- **The panel and dialogs are modal on desktop and tablet** (`role="dialog"`, `aria-modal`, labelled by their heading, focus trapped). On phones they're full pages with a back link or Close button, not modals.
- **Announcements:** the out-of-date, collecting and unavailable banners use `role="status"`. Saving a reading or component announces "Saved". There's no live region on refresh.
- **Status is never colour alone.** Pills carry words, and dots in card lines sit next to text. Watch (amber) and Near its limit (blue) differ in lightness as well as hue.
- **Charts:** each sparkline is `role="img"` with a text label giving the range, average, peak and review level. The memory bar is labelled with all three parts. The slot map is labelled with the slot line.
- **Touch targets** are at least 44px on phones.
- **Links:** "Change in Settings" goes to the review-levels settings. "connect it in Settings" goes to that server's connection settings. "Plan an upgrade" goes to Planning, prefilled. `/hardware?server=<id>&component=<id>` opens that server with the panel open.

## Out of scope

- Proxmox, generic Linux and BMC/Redfish collectors.
- The final UX for unsupported metrics. **The owner will design it.** Ship the placeholder as drawn.
- Replacement plans or lifespan estimates for components.
- Network throughput, disk I/O and per-app usage.
- Synology storage on the Storage page. That's a separate package if wanted.
- Alerts or notifications on status changes. Status shows on the page only.

## Open questions

- None blocking. Codex decides the Synology access method (002.17) and the exact TrueNAS reporting calls (002.08), and records both in **Implementation notes**.

## Acceptance checklist

- [ ] Every state in the table is implemented and reachable.
- [ ] Matches the mock at desktop (≥ 1100px), tablet (~900px), and mobile (390px), in light and dark.
- [ ] No horizontal page scroll at 390px. The component table becomes cards rather than scrolling.
- [ ] Axe passes WCAG 2.1 AA in light and dark (extend `src/Web/e2e`).
- [ ] Unknown, stale, unsupported and entered data look different from collected, healthy data.
- [ ] No claims beyond the evidence: no failure dates or probabilities, and no "Previous" figure for an unsampled period.
- [ ] Every flagged feature is completely absent when its flag is off, and the page still reads correctly without it.
- [ ] Feature-specific checks:
  - [ ] 002.01 Switching servers never shows another server's metrics or components, even briefly.
  - [ ] 002.02 An out-of-date server shows the banner, "last known" captions and "Unknown" for Needs a look.
  - [ ] 002.05 The coverage line matches the data: partial history, gaps and downsampling are all stated.
  - [ ] 002.06 Memory "used" excludes cache on both platforms; the cache label follows the platform.
  - [ ] 002.07 Changing a review level updates the dashed lines, card lines and statuses.
  - [ ] 002.08 Only read methods listed in `NAS_READ_MANIFEST.md` are called, with tests, and the flag stays off until merged and tested.
  - [ ] 002.09 Readings are drawn as dots, labelled "entered by you", and never averaged into collected data.
  - [ ] 002.11 Detected values can't be edited; a detected/entered mismatch produces the Act now status and the mismatch line.
  - [ ] 002.12 The status rules table is implemented in order, with unit tests for each row and for both card-pill thresholds.
  - [ ] 002.17 The Synology connection is read-only, with its boundary documented; Synology rows are absent with the flag off.
  - [ ] `/hardware?server=<id>&component=<id>` deep links work.

## Implementation notes

_Codex: one entry per feature ID as it lands: final endpoints and fields, components touched, deviations from the mock or fixtures and why, and test coverage._

Guidance from design:
- The mock adds three tokens: `--info-bg`, `--info-border` and `--cache`. The app has `--tone-info` for text only. Either add info background/border tokens to `index.css` (light and dark, AA-checked), or map Near its limit to an existing token pair with a visibly different lightness from Watch, and record the choice here.
- The mock's data script is a renderer, not a contract. Follow the fixtures and this brief for the data shape.

### 002.01

—

## Change log

| Date | Change | By |
| --- | --- | --- |
| 2026-11-01 | Package created at lock-in | Claude |
