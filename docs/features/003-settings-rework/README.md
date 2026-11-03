# 003 · Settings rework

- **Status:** ready · 2026-11-02
- **Mock:** [mock.html](mock.html). Use the toolbar to switch State, Width and Theme. The sub-nav, and the Manage and Review buttons on connections, work inside the mock. `?section=` opens a section directly, e.g. `?state=dirty&section=monitoring`.
- **Shared preview:** —
- **Affected screens:** **Settings** (`/settings`), which gains one URL per section: `/settings/connections`, `/settings/connections/<id>`, `/settings/monitoring`, `/settings/review-levels`, `/settings/notifications`, `/settings/data`, `/settings/display`. `/settings` redirects to `/settings/connections`.

## Goal

The owner can find and change any setting in a few seconds, see straight away which systems need attention, and trust that a change was saved (or know exactly why it wasn't). The current page is one 3,300px scroll grouped by component, with a single save button for unrelated fields and long caveats detached from the fields they describe.

## Design summary

- **Six sections, one at a time.** A left sub-nav on desktop and tablet; a horizontally scrolling tab row on phones. Sections are grouped by the owner's task, not by backend component:
  1. **Connections:** the systems Homelab Control reads.
  2. **Monitoring:** how often it reads drives, and the optional reads.
  3. **Review levels:** the owner's own levels for when something is worth a look.
  4. **Notifications:** where findings go.
  5. **Data & backups:** history retention and Homelab Control's own backups.
  6. **Display:** theme, timezone, units.
- **One row per setting.** Label and a one-line hint on the left, control and unit on the right (stacked below 1100px). Long caveats move into a "How this works" disclosure under the row they belong to. None of the existing caveat copy is dropped; it's shortened in the hint and kept whole in the disclosure.
- **Each section saves on its own.** Editing shows a sticky bar ("1 unsaved change in Monitoring", Discard, Save changes), a dot on that section in the nav, and "Was 90 minutes" under each changed field. A failed save keeps the edits, turns the bar red and puts the message on the field. There is no page-wide save button.
- **Connections become a list plus a detail page.** The list shows each system's status pill and one meta line. Everything about one connection (address, tunnel, interval, certificate trust, API key, identity review) moves to its own page, replacing today's dialog and the empty "Manage connection identity & certificate trust" heading.
- **Notifications setup is three visible steps:** choose a destination, send a test, turn on delivery. Later steps are disabled with a reason until the earlier one succeeds. Once set up, the steps collapse to a summary with Change, Send another and a delivery switch.
- **Theme still applies immediately** and is saved in the browser. It never needs the save bar.
- **Actions are not settings.** "Back up now" and "Run now" sit next to what they act on and never wait for a save.

Rejected alternatives:
- **One long page with an anchored table of contents.** Rejected: it keeps the length and the ambiguity about what "Save" covers.
- **Autosave on every change.** Rejected: interval and threshold edits change what the app reports, so the owner confirms them. Theme is the only instant setting.
- **Keeping the connection dialog.** Rejected: certificate trust and identity review need room and a stable URL.

## Features

| ID | Feature | Depends on | Flag | Status |
| --- | --- | --- | --- | --- |
| 003.01 | Settings shell: sub-nav (desktop/tablet) and tab row (phone), one section at a time, a URL per section, attention count on Connections | — | — | ready |
| 003.02 | Per-section save: sticky save bar, Discard, "Was …" hints, nav dot, leave guard, field-level errors, conflict handling | 003.01 | — | ready |
| 003.03 | Connections list: status pill, meta line, Connect NAS, Add Synology hardware, empty state | 003.01 | — | ready |
| 003.04 | Connection detail page: connection, certificate trust, replace API key, identity-change review | 003.03, 003.02 | — | ready |
| 003.05 | Monitoring: drive collection intervals; optional reads as switches with "How this works"; power-on hours last run and Run now | 003.02 | — | ready |
| 003.06 | Review levels: drives (age, warranty, local temperature policy) and hardware levels | 003.02 | — | ready |
| 003.07 | Notifications: three-step setup, set-up summary, quiet period, recent deliveries | 003.01 | — | ready |
| 003.08 | Data & backups: history retention, backup destination, daily backups, copies, Back up now, backup history | 003.02 | — | ready |
| 003.09 | Display: theme (instant, per browser), timezone, capacity units | 003.02 | — | ready |
| 003.10 | Loading and unavailable states for the whole page | 003.01 | — | ready |
| 003.11 | Remove connection (new: stops collection and deletes credentials, keeps evidence) | 003.04 | — | ready |
| 003.12 | "Last replaced" date for a connection's API key (new field) | 003.04 | — | ready |

003.11 and 003.12 are capabilities the app doesn't have today. Build or defer each on its own; when deferred, its row is simply absent from the detail page. "Add Synology hardware" (003.03) renders only while 002's `synology` flag is on.

## States

| State | When it happens | What the user sees | Fixture |
| --- | --- | --- | --- |
| Normal | Settings, connections, notifications and backups loaded | Connections list with one system needing review; other sections filled in | `settings.json`, `connections.json`, `notifications.json`, `backups.json` |
| Connection detail | Owner opens Manage or Review on a connection | "All connections" back link, name and status, identity-change callout when relevant, then Connection, Certificate trust, API key and Remove groups | `connection-detail.json` |
| Unsaved changes | Owner edits a field in a section | Save bar, nav dot, "Was …" under changed fields | `settings-patch.json` |
| Save failed | Server rejects a section save | Red bar "Not saved. Fix the highlighted field and try again."; field outlined with its message; edits kept | `save-error.json` |
| First run | Nothing set up yet | Connections empty state; power-on hours "Never run"; Notifications "Not set up" with step 1 open; backups "No backup destination configured" | `connections-empty.json`, `notifications-empty.json`, `backups-unconfigured.json` |
| Loading | First load | Nav (disabled) plus skeleton heading and group; "Loading settings…" status | — |
| Unavailable | Settings request failed | Error callout naming the failure, with Try again; note that monitoring keeps running with the last saved settings; Display still works | — |
| Mobile (≤ 700px) | Narrow screens | Tab row scrolls sideways; rows stack; save bar buttons share the width; connection actions drop below the meta line | — |

Connection status pills follow the app's tones, each with icon and label: Reporting (ok), Out of date (warn), Identity changed (bad), Paused (neutral, dashed), plus the existing unavailable and never-collected states.

## Copy

Use the mock's wording. Key strings:

- Page subtitle: "How Homelab Control reads your systems, what it flags for review, and where it sends alerts."
- Connections: "The systems Homelab Control reads from. Connections are read-only and never change NAS configuration."
- Empty connections: "No systems connected yet" / "Connect a TrueNAS system directly or through an SSH tunnel you already run. Until then, every health status stays unknown."
- Monitoring: "How often Homelab Control looks at drives, and which optional reads it makes. Every read is read-only."
- Review levels: "Your own levels for when something is worth a look. They aren't failure predictions or manufacturer safety limits."
- Notifications: "Where findings are sent. Delivery turns on only after a successful test." Test result: "Accepted means received by Telegram, not read." (use the receiver's name).
- Save bar: "{n} unsaved change(s) in {Section}" / failure "Not saved. Fix the highlighted field and try again."
- Unavailable: "Settings couldn't be loaded" + the failure reason + "Monitoring keeps running with the last saved settings; nothing here has changed."
- Identity change: "This system reported a different serial" + both serials + "Collection is paused so evidence from two machines isn't mixed. Historical evidence is kept either way."

"How this works" disclosures carry today's full caveats (dataset limits, power-on hours waking drives and NVMe/SAS support, cache values lacking observation times, cleanup and backup behaviour). Keep their meaning exactly; tighten wording only.

## Data needs

Fixtures group settings by section. Today the API exposes one flat settings object; Codex decides whether to group it.

| Feature | Need | Proposed field(s) | Notes |
| --- | --- | --- | --- |
| 003.01 | Systems needing attention | `connections.attentionCount` | Count of identity-changed or unavailable connections |
| 003.02 | Save one section without touching others | `PATCH /api/v1/settings/{section}` with `revision` and `changes` | 400 returns `errors` keyed by field; 409 when the revision is stale |
| 003.03 | Per-connection state and freshness | `state`, `lastSuccessUtc`, `pausedUtc`, `pausedBy`, `tunnel` | `pausedBy` tells "paused by you" from a system pause |
| 003.04 | Identity comparison | `identity.savedSerial`, `identity.observedSerial` | As today |
| 003.05 | Last power-on hours run | `monitoring.powerOnHoursLastRun` | `outcome`: complete, partial, failed; null when never run |
| 003.07 | Channel summary without secrets | `channel.summary` | Never return tokens or full URLs |
| 003.08 | Backup destination status | `destination.configured`, `destination.path` | As today, plus the path for display |
| 003.11 | Remove a connection | `DELETE /api/v1/connections/{id}` | Must keep drive history and evidence |
| 003.12 | When the key was last replaced | `apiKey.replacedUtc` | Null when unknown; then omit the line |

## Interaction and accessibility

- Keyboard path: sub-nav buttons, then the section heading, then fields in reading order, then the save bar. Moving to a section focuses its `h2`.
- The sub-nav is `nav aria-label="Settings sections"`; the current item has `aria-current="page"`. The nav dot has an accessible name ("Unsaved changes").
- The save bar is a labelled region. A save failure is announced (`role="alert"`) and focus moves to the first invalid field, which has `aria-invalid` and `aria-describedby` pointing at its message.
- Leaving a section, or the page, with unsaved changes asks to discard or stay.
- Switches are real checkboxes with labels; segmented controls are radio groups.
- Status is never colour alone: every pill and callout has an icon and a label; neutral states use dashed borders.
- Touch targets are at least 40px in the tab row and 44px for primary actions on phones.

## Out of scope

- New settings, or changes to collection behaviour or limits.
- The "Add a connection" flow itself (Connect NAS opens the existing enrollment).
- Synology enrollment details (002.17 owns them).
- Restoring backups from the UI.

## Open questions

- Should Discard also run on browser back, or always ask first? Proposed: always ask first.

## Acceptance checklist

- [ ] Every state in the table is implemented and reachable.
- [ ] Matches the mock at desktop (≥ 1100px), tablet (~900px), and mobile (390px), in light and dark.
- [ ] No horizontal page scroll at 390px. Only the tab row scrolls sideways.
- [ ] Axe passes WCAG 2.1 AA in light and dark (extend `src/Web/e2e`).
- [ ] Unknown, stale, paused and unsupported states look different from healthy ones.
- [ ] No claims beyond the evidence.
- [ ] Feature-specific checks:
  - [ ] 003.01: each section has its own URL; reload and back/forward keep the section.
  - [ ] 003.02: saving one section never changes fields in another; a stale revision shows a conflict instead of overwriting; Discard restores the saved values.
  - [ ] 003.04: the identity callout appears only when the serials differ; Accept new baseline is unavailable while the observed serial is missing.
  - [ ] 003.05: Run now works without saving other edits, and is disabled until power-on hours is saved as on.
  - [ ] 003.06: temperature limit and basis appear only while the policy switch is on; basis is required when it is on.
  - [ ] 003.07: Send a test and Turn on delivery are disabled, with their reason shown, until the previous step succeeds; changing the destination requires a new test.
  - [ ] 003.08: Back up now is absent when no destination is configured.
  - [ ] 003.09: theme applies without the save bar; timezone and units use it.
  - [ ] 003.11: removing asks for confirmation and keeps drive history.

## Implementation notes

_Codex: one entry per feature ID as it lands: final endpoints and fields, components touched, deviations from the mock or fixtures and why, and test coverage._

### 003.01

—

## Change log

| Date | Change | By |
| --- | --- | --- |
| 2026-11-02 | Package created | Claude |
