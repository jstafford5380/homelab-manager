# 005 · Planning redesign

- **Status:** implemented · 2026-11-04
- **Mock:** [mock.html](mock.html). Use the toolbar to switch State, Width and Theme, e.g. `?state=stale&width=mobile&theme=dark`.
- **Shared preview:** https://claude.ai/artifact/DxLkodeuCcqL2vZVWbJdvT (the exploration version; `mock.html` is the final one)
- **Affected screens:** **Planning** (`/planning`): the whole page. Today that's `PlanningReview.tsx`, `SpareReview.tsx`, the plan queue in `App.tsx`, and the plan dialogs (`PlanEditor.tsx`, the "Plan ahead" dialog).

## Goal

The owner opens Planning and sees what to decide next, without opening anything. Which drives need a decision and why, what's planned or on order and when it's due, and which spares fit which drives. The page should read like a to-do board in colour, not a set of disclaimers.

## Design summary

Today's page has three problems the owner called out:

- **Expanders hide the point.** "Why this drive is listed", "Review spare compatibility", "Evidence & notes" and "Show all drive reviews" all need a click before they show anything useful.
- **The copy explains instead of directing.** About eight caveat paragraphs ("Stock is not matched to drives automatically…", "Manual estimates, grouped by currency…") sit above or between the actions, and each drive's next step is a long sentence.
- **Everything is grey.** Only the media-evidence badge has colour.

The redesign:

- **Summary strip first (005.01).** Three blocks in one card. *Drives to review*: a count out of tracked drives, a stacked bar of the three priorities, and a legend. *Open plans*: a count, plus overdue and due-soon pills. *Estimated spend*: the total, a bar split into ordered and planned, and a "without an estimate" count. Every legend item and pill links to the section below it.
- **Drives to review is a flat list with nothing collapsed (005.02, 005.03).** Each reason is a short chip on the row, coloured by meaning, with the full rule text in a Tooltip. Where the fix is a record you can complete, the chip carries a link to it: *Add date*, *Locate*, *Resolve*. Rows for media evidence and observed issues are full height, with a priority label, the drive's location, a one-line **Next:** step, and the spare-fit pill. Prepare-ahead rows are one line. Every review is shown, so "Show all" goes away.
- **Each row has one main action, chosen by priority (005.03).** *Plan replacement* (primary button) for media evidence. *Plan a review* (outline) for observed issues. *Add to plan* (outline) for prepare ahead. If the drive already has an open plan, the action becomes an *In plan · Planned/Ordered* pill that scrolls to that plan and focuses it.
- **Spare fit sits on the row (005.04).** Today a per-drive expander loads its compatibility history. Now a pill shows the latest result: *Spare fits · <name>*, *Spare not checked*, *Spare check expired*, *Spare doesn't fit*, or *No spares recorded*. *Check a spare* opens today's compatibility-review dialog directly, and that dialog also lists earlier reviews for the drive.
- **Plans are a board (005.05).** Three columns: Planned (blue), Ordered (violet), Done · last 30 days (green). Each card has the title, a coloured due pill (overdue red, due within 14 days amber, later neutral), the linked drive's serial, the owner, the notes clamped to two lines, and the estimate. A card with no estimate shows an *Add estimate* pill instead. One click moves a plan forward: *Mark ordered →*, *Mark done →*. The pencil opens the editor with the full notes, the attached evidence, status (including moving a plan back), and history. Cards sort by due date, and cards without one come last.
- **Spares are on the right (005.06).** A coloured quantity tile (green when on hand, amber at 0), the name, how many drives it's been checked against, the unit price, and *Manage in Inventory*.
- **One footer line holds the caveats (005.07).** The rules, thresholds, rule version and generated time all go there, along with the "don't predict failures" statement. Specific caveats move into the tooltips of the chips they apply to.
- **New colours (005.08).** Info gets a background and border token, and a violet *progress* tone is added for Ordered. Bar colours are a little more saturated than the pill text tones so the thin bars stay visible.
- **The create-plan dialog shows evidence as chips (005.09).** The drive's reasons appear as read-only chips under "Evidence attached", not as a text blob appended to the notes. Notes, estimate, due date and owner are separate fields.

Rejected alternatives:
- **A table of drives with sortable columns.** Rejected: reasons vary in number and kind, so they belong in chips, not cells.
- **Tabs for the plan statuses.** Rejected: a tab is an expander in another form. The owner wants to see everything at once.
- **Keeping the per-drive "Why" list collapsed by default.** Rejected: the reasons are the content.
- **A Dismiss or Snooze action on reviews.** Out of scope: the API has no such state, and the page won't imply one.
- **A coloured accent bar on the left of each row.** Rejected in favour of a tinted icon tile. The media row also gets a faint tint behind it.

## Features

| ID | Feature | Depends on | Flag | Status |
| --- | --- | --- | --- | --- |
| 005.01 | Summary strip: drives to review (stacked bar + legend), open plans (overdue and due-soon pills), estimated spend (ordered/planned bar, without-estimate count) | 005.08 | — | implemented |
| 005.02 | Review rows: reason chips with tooltips and fix links, priority label, location, Next line; prepare-ahead rows on one line; every review shown | 005.08 | — | implemented |
| 005.03 | Main action per priority; *In plan · status* pill that links to the plan | 005.02, 005.05 | — | implemented |
| 005.04 | Spare-fit pill on each review row; *Check a spare* opens the compatibility dialog, which includes that drive's review history | 005.02 | — | implemented |
| 005.05 | Plans board: Planned / Ordered / Done (last 30 days), due pills, one-click advance, edit dialog for everything else | 005.08 | — | implemented |
| 005.06 | Spares rail: quantity tile, checked-drive count, unit price, Inventory link | 005.08 | — | implemented |
| 005.07 | Single rules footer; remove the other planning caveat paragraphs | 005.02 | — | implemented |
| 005.08 | Tokens: `--tone-info-bg`, `--tone-info-border`, `--tone-progress` / `-bg` / `-border`, and bar colours, in light and dark | — | — | implemented |
| 005.09 | Create-plan dialog: evidence attached as chips; notes, estimate, due and owner as separate fields | 005.02 | — | implemented |

## States

| State | When it happens | What the user sees | Fixture |
| --- | --- | --- | --- |
| Normal (mixed) | Five reviews (1 media, 1 issue, 3 prepare), three open plans (one overdue, one due in 4 days), one done this month, two spares | Everything as described above | `fixtures/normal.json` |
| Calm | No reviews. One open plan and one done plan | Summary bar is empty and reads "No reminders from the current rules". Drives to review is one quiet card: "No drives match the planning rules." / "This isn't a health check. Check coverage in Storage." | `fixtures/calm.json` |
| Empty | No tracked drives, plans or spares | No summary. Three get-started cards: *Connect a NAS*, *Record your spares*, *Add a plan*. Footer still shows | `fixtures/empty.json` |
| Loading reviews | Plans are loaded (they come with the app snapshot) but `/planning/review` hasn't answered | Drive and spend blocks and the spares rail show skeletons. Three skeleton rows. The plans board is fully rendered | `fixtures/loading.json` |
| Reviews unavailable | `/planning/review` failed with no earlier data | Summary drive block reads "Unknown" with a *Reviews unavailable* pill. A red banner with the error, "Your plans below are current.", and *Retry*. Spares: "Unavailable with drive reviews." The board is fully rendered | `fixtures/unavailable.json` |
| Stale reviews | A refresh failed after an earlier load succeeded | An *Out of date* pill on the summary drive block. An amber banner: "Showing reviews from 4:31 PM. Refresh failed at 4:58 PM. Plans you add now still save." with *Retry*. Rows stay visible | `fixtures/stale.json` |
| Create plan dialog | *Plan a review* on the IronWolf row | Dialog as in 005.09. The estimate is empty because that drive has no compatible spare check | `fixtures/dialog.json` |
| Tablet (700–1100px) | | One column. The spares rail goes under the queue as a two-column grid. The board stays three columns | — |
| Mobile (≤ 700px) | | Summary: drives block full width, plans and spend side by side under it. Review rows stack, with actions below the chips. The board is one column. Dialog fields are one column | — |

## Copy

- **Page:** heading "Planning", sub-line "Turn observations into a practical replacement plan." (unchanged), header button "Add plan".
- **Summary (005.01):** labels "Drives to review", "Open plans", "Estimated spend". "<n> of <tracked> tracked". Legend "<n> media evidence", "<n> observed issue", "<n> prepare ahead". Pills "<n> overdue", "<n> due in 2 weeks". With neither: "Nothing due in the next 2 weeks". Spend legend "<money> ordered", "<money> planned", "<n> without an estimate". With more than one currency, show one spend block per currency, stacked.
- **Priority labels (005.02):** "Media evidence" (bad, triangle-alert), "Observed issue" (warn, circle-alert), "Prepare ahead" (info, calendar).
- **Next lines (005.02).** These are UI copy chosen by priority, replacing the API's long `nextStep` sentence:
  - Media evidence: "Check recovery readiness, then line up a compatible replacement."
  - Observed issue: "Review the evidence and the drive's path (cable, port, backplane) before deciding."
  - Prepare ahead: no Next line.
  - The rule-specific long sentences today (self-test, NVMe warning, NVMe endurance) go into the tooltip of the matching finding chip.
- **Reason chips (005.02).** The tooltip for each is the full rule text, today's `reasons[].detail`.
  - `finding`: the finding's short summary, e.g. "12 pending sectors, up from 0", then the link "Evidence" to `/?finding=<id>`. Tone follows the row's priority.
  - `warranty` ending: "Warranty ends <Mon D> · <n> days" (warn). Ended: "Out of warranty since <Mon YYYY>" (neutral).
  - `age-review`: "<n> years since purchase", with " (estimated)" when the date confidence is estimated (info).
  - `age-unknown`: "Purchase date unknown", with the link "Add date" to the drive's hardware record (neutral, dashed).
  - `absent`: "Missing from last inventory", with the link "Locate" to the drive in Storage (warn).
  - `ambiguous`: "Identity ambiguous", with the link "Resolve" to the drive in Storage (warn).
- **Row actions (005.03):** "Plan replacement", "Plan a review", "Add to plan". In a plan: "In plan · Planned" / "In plan · Ordered".
- **Spare fit (005.04):** "Spare fits · <spare name>" (ok; tooltip "Your compatibility check. Review again by <date>. A check doesn't reserve a unit."), "Spare not checked" (neutral, dashed), "Spare check expired" (warn), "Spare doesn't fit" (bad), "No spares recorded" (neutral, dashed). Button "Check a spare". It isn't shown when no spares are on hand, or when the spare fits.
- **Board (005.05):** heading "Plans", button "Add plan". Columns "Planned", "Ordered", "Done · last 30 days". Empty columns: "Nothing planned", "Nothing on order", "Nothing finished recently". Due pills: "Overdue <n> day(s)", "Due <Mon D> · <n> day(s)", "Due <Mon D>". "Unassigned" when there's no owner. "Add estimate". Buttons "Mark ordered", "Mark done", and the Edit icon button, labelled "Edit <title>". Done card: "Done <Mon D> · <money>".
- **Spares (005.06):** heading "Spares". "Checked for <n> drive(s)" (ok text) or "Not checked against any drive". "<money> each". "No spares recorded." Link "Manage in Inventory".
- **Footer (005.07):** "**Advice only.** Reminders come from your records and observed evidence. They don't predict failures." · "Review after **<n> years** · warranty notice **<n> days** · Change in Settings" · "<ruleVersion> · generated <time>".
- **Calm:** "No drives match the planning rules." / "This isn't a health check. Check coverage in Storage."
- **Empty:** the three cards. "Connect a NAS": "Drive reviews appear after the first inventory. They flag evidence, warranty dates and drive age." "Record your spares": "Add spare drives in Inventory so you can check which drives they fit." "Add a plan": "Track a purchase, replacement or maintenance task with a budget and a date."
- **Errors:** unavailable banner: "<error message> Your plans below are current." with Retry. Stale banner: "Showing reviews from <time>. Refresh failed at <time>. Plans you add now still save." with Retry.
- **Dialog (005.09):**
  - Title: "Plan replacement", "Plan a review" or "Add to plan", matching the button. Sub-line: "Saved in Homelab Control. Nothing is ordered and the NAS isn't changed."
  - Fields: "Title" (default "Review <label> <serial>", or "Replace <serial>" for media evidence), "Evidence attached", "Notes" (placeholder "What you'll check, and what would change the plan"), "Estimate (optional)", "Due (optional)", "Owner (optional)".
  - Estimate hint: "From your Inventory price for <spare>" when a compatible spare check exists. Otherwise "Check a spare to fill in its Inventory price."
  - Due hint: "Two weeks from today" for media and issue rows. Prepare-ahead rows start with an empty due date.

Truthfulness: every chip is a recorded fact (a finding, a date, an inventory state). The Next lines are suggested steps, not predictions. "Spare fits" always means "you recorded this", and its tooltip says so. The Calm state says outright that it isn't a health check.

## Data needs

Proposed shape is in `fixtures/`. `review` is the proposed `GET /api/v1/planning/review`. `plans` is the plan list, as in today's snapshot. `_mock` is mock-only. Codex owns the real contract.

| Feature | Need | Proposed field(s) | Notes |
| --- | --- | --- | --- |
| 005.02 | Priority as a key | `reviews[].priority`: `media` \| `issue` \| `prepare` | Today it's display text. Keep the same sort |
| 005.02 | Structured reasons | `reasons[]`: `kind` (`finding`, `warranty`, `age-review`, `age-unknown`, `absent`, `ambiguous`), plus `findingId` + `summary` for findings, `endsUtc` for warranty, `purchasedUtc` + `confidence` for age. `detail` stays for the tooltip | Today `absent` and `ambiguous` share `kind: "identity"`. Split them |
| 005.02 | Friendly name and location | `reviews[].label` (fall back to `model`), `reviews[].location` (pool · vdev, or "last seen in …" when absent; null hides it) | Codex's choice where these come from |
| 005.03 | The open plan's status | `reviews[].openPlan`: `{ id, status }` or null | Replaces `openPlanIds` |
| 005.04 | Latest spare check per drive | `reviews[].spareFit`: `{ status, assetName, validUntilUtc }`, status `compatible` \| `incompatible` \| `expired` \| `unchecked` | Today's status rules (expired, superseded, invalidated by inventory edits) apply. History stays on `/disks/{id}/spare-reviews` for the dialog |
| 005.06 | Checked-drive count per spare | `spares[].compatibleDriveCount` | Current, unexpired `compatible` reviews only |
| 005.01 | Spend split | `budgets[]`: `{ currency, planned, ordered, withoutEstimate, count }` | Open plans only, as today |
| 005.05 | Done date | `plans[].completedUtc` | For "Done · last 30 days". Plan history may already have it |
| 005.05 | Drive serial on a plan | `plans[].diskSerial` (or join on `diskId`) | |
| 005.05, 005.09 | Notes separate from attached evidence | `plans[].notes` and attached reasons (e.g. `plans[].evidence[]` with the same shape as `reasons[]`) | Today both go into one `reason` text field. Existing plans can show their whole `reason` as notes |
| 005.01 | Overdue and due-soon | `plans[].dueUtc` | Compute in the UI, in the display timezone. Due soon = within 14 days |

## Interaction and accessibility

- **Focus order** follows the visual order: header button, summary links, review rows (chips with links and tooltips, then row actions), spares rail, board columns left to right, footer. The rail comes after the queue in the DOM.
- **Tooltips** use shadcn Tooltip on focusable chips, so they open on keyboard focus as well as hover. A chip with a link keeps the link as its own tab stop.
- **The In-plan pill** moves focus to the plan card (`tabIndex=-1`), as today's `?plan=` focus does.
- **One-click advance** announces the result in a polite live region, e.g. "Moved to Ordered", and keeps focus on the moved card in its new column.
- **Status is never colour alone.** Every pill and chip has an icon and words. Bars have an `aria-label` that lists their counts, and the legend repeats them in text.
- **Rows** are `article`s with an `aria-label` of the drive name, serial and priority. Board columns are labelled regions. Headings: `h2` for each section, `h3` for the drive name and plan title.
- **Skeletons** sit in a container with `aria-busy="true"`. The error banner is `role="alert"`. The stale banner is `role="status"`.
- **Links:** finding Evidence goes to `/?finding=<id>`. Add date goes to the drive's hardware record. Locate and Resolve go to the drive in Storage. Coverage goes to `/storage`. Manage in Inventory goes to `/inventory`. Change in Settings goes to the planning thresholds in Settings. Connect a NAS goes to Settings → Connections.

## Out of scope

- New planning rules, thresholds, or snooze/dismiss states.
- Drag-and-drop on the board. The advance buttons and the editor cover status changes.
- Changing the Inventory or Settings pages.
- Pricing lookups or purchase totals beyond the user's own estimates.

## Open questions

None.

## Acceptance checklist

- [x] Every state in the table is implemented and reachable.
- [x] Matches the mock at desktop (≥ 1100px), tablet (~900px), and mobile (390px), in light and dark.
- [x] No horizontal page scroll at 390px.
- [x] Axe passes WCAG 2.1 AA in light and dark (extend `src/Web/e2e`), including the new tones on their backgrounds.
- [x] Unknown, stale, and unsupported data look different from healthy data.
- [x] No claims beyond the evidence (no failure dates or probabilities).
- [x] No `<details>` or show-more toggles remain on the Planning page.
- [x] Feature-specific checks:
  - [x] 005.01: the bar segments and legend counts match the review list. Overdue and due-soon pills count only open plans. With no reviews, the bar is empty and the legend reads "No reminders from the current rules".
  - [x] 005.02: every reason renders as a chip with its full detail in a keyboard-reachable tooltip. Prepare-ahead rows are a single line at desktop. All reviews render, with no show-all toggle.
  - [x] 005.03: a drive with an open plan shows the In-plan pill with that plan's status, not a create button. Activating the pill focuses the plan card.
  - [x] 005.04: the spare-fit pill matches the latest valid review for that drive (expired and superseded rules as today). *Check a spare* opens the dialog with the drive's history, and is hidden when there are no spares on hand.
  - [x] 005.05: Mark ordered and Mark done update the status with one request and move the card. Done shows only plans completed in the last 30 days. Due pills use the display timezone.
  - [x] 005.06: the quantity tile is amber at 0. The checked-drive count counts only current compatible reviews.
  - [x] 005.07: the footer is the only general caveat on the page. The thresholds and rule version match the API.
  - [x] 005.08: the new tokens exist in light and dark in `index.css`, and pills using them pass contrast.
  - [x] 005.09: saving a plan stores the notes and the attached evidence separately. The estimate is pre-filled only when a compatible spare with a unit price exists.

## Implementation notes

_Codex: one entry per feature ID as it lands: final endpoints and fields, components touched, deviations from the mock or fixtures and why, and test coverage._

### 005.01

`PlanningReview.tsx` renders the three summary blocks, linked priority legends and bars, open-plan due counts and per-currency spend. Reviews come from `GET /api/v1/planning/review`; open-plan counts and spend use the current overview snapshot. The review API also exposes `budgets[].planned` and `.ordered` alongside the existing totals. Due counts use calendar days in the configured display timezone.

Deviation: when reviews are unavailable, spend remains available from the independently loaded plans instead of becoming unknown as in the mock. Loading still uses the specified skeleton. The browser suite covers normal/calm/empty/loading/unavailable/stale states, mixed currencies and a UTC timestamp that falls on the previous display day.

### 005.02

`PlanningReason` now supplies `summary`, `endsUtc`, `purchasedUtc` and `confidence`; identity reminders are split into `absent` and `ambiguous`. `DriveReview` adds `priorityKey`, `label` and `location`, retaining the old priority text and `nextStep` for existing clients. Label uses the recorded nickname with model fallback; location uses the recorded bay, prefixed with “Last seen in” for absent drives. No location is inferred from uncertain topology. `ReasonChips.tsx` uses shadcn Tooltip on focusable chip text, with separate fix/evidence links. Rule-specific self-test and NVMe advice is retained in finding tooltips.

Deviation: Add date opens the drive’s existing Storage record, where Cost & lifespan contains the purchase-date editor; the Hardware page now contains server components rather than drive records. Compact rows wrap when their full facts/actions cannot fit. Recorded purchase/warranty dates keep their calendar date; plan timestamps use the display timezone. Tests cover all five reviews, keyboard tooltips, date labels, breakpoints and both themes.

Follow-up (2026-11-04): Planning now matches Storage’s replacement handling. An absent drive with a non-voided replacement record is excluded from reviews and the tracked-drive count. A voided replacement or a drive that reappears in inventory remains eligible. Historical drive records, findings and plans are preserved. Four API regression cases cover those conditions (including an ordinary missing drive).

### 005.03

Each priority selects the specified primary action. Open-plan pills use the current overview plans, so failed review refreshes cannot leave an old plan status on a row. Activating a pill scrolls immediately and focuses the matching `plan-<id>` card; existing `?plan=` navigation remains supported. `openPlan: { id, status }` is also included in review responses, with old `openPlanIds` retained for compatibility. Browser tests cover focus, create-to-in-plan changes and completion removing the open-plan action.

### 005.04

`GET /api/v1/planning/review` includes `reviews[].spareFit: { status, assetId, assetName, validUntilUtc }`. It reads only the latest assessment for each drive/spare pair and applies the existing `SpareCompatibility.Status` rules. A current compatible check takes precedence across available spares; otherwise the latest assessment determines expired/incompatible/unchecked. Inventory edits, identity changes, absent/retired drives and exhausted stock cannot establish a fit.

`SpareReview.tsx` opens the existing compatibility form directly and loads paginated history inside that dialog from `/disks/{id}/spare-reviews`. No expanders remain. Checks are hidden when no units are on hand or a current fit exists. Stale planning data downgrades fit pills and checked counts explicitly, and expiry is rechecked while the page is open.

Deviation: spare-fit pills and check actions also remain visible on prepare-ahead rows, as required by the brief, although the mock omits them there. This can make those rows taller. API tests cover supersession, invalidation and checked counts; browser tests cover history, compatible/expired/incompatible/unchecked/no-spares states and zero stock.

### 005.05

`PlansBoard.tsx` replaces the old queue with labelled Planned, Ordered and Done regions. Cards sort by due date, with undated cards last; notes clamp to two lines. `PlanEditor.tsx` provides the full notes, attached evidence, backward status changes and visible history. Advance uses one revision-checked `PUT /api/v1/plans/{id}`, announces the move and focuses the moved card. Successful create/edit/advance responses update the app snapshot before refreshing, so a subsequent refresh failure does not hide a saved change.

`PlanningItem.completedUtc` is server-owned: entering complete sets it, editing a complete plan preserves it, reopening clears it. Migration `PlanningBoard` backfills only recorded completion transitions from plan history. Earlier complete plans without a known completion time remain unknown and are excluded from the recent-done board; no date is invented. Drive serials are joined from overview disks. Done cards retain an Edit control so completed plans can be reopened.

Tests cover persisted completion/reopen/history, migration preservation, the 30-day window, unknown completion dates, due-day boundaries, one-request movement/focus, edit conflicts and successful saves followed by refresh failure.

### 005.06

The spares rail uses `spares[].compatibleDriveCount`, counting current compatible assessments once per drive using the same validity rules as row fit pills. Quantity is green above zero and amber at zero, with optional Inventory unit prices and the Inventory link. The rail follows reviews in the DOM; its items form a two-column grid at tablet widths and one column on mobile. Backend tests verify supersession/inventory invalidation; browser tests cover counts, prices, zero quantities and missing inventory.

### 005.07

`PlanningReview.tsx` has one general advice/rules footer, showing the API’s thresholds, rule version and generated time, with a link to `/settings/review-levels`. When reviews have never loaded, thresholds are explicitly unavailable rather than guessed. Other general planning caveat paragraphs and every Planning `<details>`/show-more control are removed. Specific compatibility caveats remain inside that assessment dialog. All state tests check the page has no details controls.

### 005.08

`index.css` adds info background/border, progress foreground/background/border, and saturated `--bar-bad`, `--bar-warn`, `--bar-info`, `--bar-progress` tokens in light, explicit dark and system-dark themes. `Planning.css` maps the layout to the app’s 700px/1100px viewport breakpoints using existing Card/Button/Tooltip components. Mobile keeps the full Add plan label. Axe WCAG 2.1 AA checks cover every designed state at 1440px, 900px and 390px in both themes; screenshots and overflow assertions accompany the checks.

### 005.09

`CreatePlanDialog.tsx` is shared by generic Add plan and drive-specific actions. Notes, owner, estimate/currency and due date are separate inputs; read-only evidence chips are attached separately. `POST /api/v1/plans` accepts `notes` and structured `evidence[]`. Notes alias the existing stored `Reason` column for backward compatibility, while a new `EvidenceJson` column stores attachments independently. Existing Reason text is preserved verbatim as notes. Attachments remain immutable on later edits and are retained in history.

Only a current compatible check with an on-hand Inventory asset and unit price pre-fills the estimate. Media/issue due dates default to two weeks from the display calendar date; prepare-ahead starts empty. Date-only input is stored at noon in the configured timezone rather than assuming UTC or the browser timezone. Currency is a separate labelled input, preserving the project’s existing free three-letter currency support. Backend/browser tests verify evidence/notes separation, immutable attachments, empty vs sourced estimates, owner, due defaults and round-tripped timestamps.

### Verification · 2026-11-04

- Web production build and solution build pass.
- Full backend suite: 877 tests pass, including Planning API, immutable evidence, completion transitions, compatibility aggregation and migration preservation.
- `e2e/planning.spec.ts`: 10 tests pass. Seven designed states each receive axe WCAG 2.1 A/AA checks in light/dark at 1440px, 900px and 390px, screenshots and no-overflow assertions. Interaction cases cover focus, keyboard tooltips, separate notes/evidence, spare histories/statuses, mixed currencies, dates and a saved status move followed by overview refresh failure.
- Updated `e2e/workflows.spec.ts` passes against the new controls, including actual create/edit/history/conflict workflows and compatibility assessment.
- Lint completes with existing warnings; `git diff --check` passes.
- Local API restarted through Aspire. Implementation is in the working tree, ready for Claude’s design review; deliberate visual/contract differences are recorded above. Mocks and design fixtures are unchanged.

## Change log

| Date | Change | By |
| --- | --- | --- |
| 2026-11-04 | Package created | Claude |
