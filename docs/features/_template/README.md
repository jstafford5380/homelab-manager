# NNN · Feature name

- **Status:** draft · YYYY-MM-DD
- **Mock:** [mock.html](mock.html)
- **Shared preview:** — (link to the published mock, if there is one)
- **Affected screens:** e.g. Overview → "Coming up"

## Goal

What the owner should be able to notice, decide, or do after this ships, in one or two sentences. Tie it to the product aim: staying ahead of problems instead of reacting to them.

## Design summary

The main layout and interaction decisions, and why. Note any alternatives considered and rejected, so they don't get re-proposed.

## States

Each row needs a matching option in the mock's state switcher, and a fixture if it needs data.

| State | When it happens | What the user sees | Fixture |
| --- | --- | --- | --- |
| Normal | | | `fixtures/normal.json` |
| Empty | | | |
| Loading | | | — |
| Unavailable / error | | | |
| Stale or unknown data | | | |
| Mobile (≤ 700px) | | Layout notes | — |

## Copy

The final wording for headings, labels, empty states, and caveats. If the wording is a claim about health or the future, explain what evidence supports it.

## Data needs

What the UI needs to render each state. `fixtures/` holds a proposed shape. Codex owns the real contract.

| Need | Proposed field(s) | Notes (units, nullability, freshness) |
| --- | --- | --- |
| | | |

## Interaction and accessibility

- Keyboard path and focus order:
- Live regions or announcements:
- Status is never shown by colour alone (icon + label):
- Links and destinations:

## Out of scope

## Open questions

## Acceptance checklist

- [ ] Every state in the table is implemented and reachable.
- [ ] Matches the mock at desktop (≥ 1100px), tablet (~900px), and mobile (390px), in light and dark.
- [ ] No horizontal page scroll at 390px.
- [ ] Axe passes WCAG 2.1 AA in light and dark (extend `src/Web/e2e`).
- [ ] Unknown, stale, and unsupported data look different from healthy data.
- [ ] No claims beyond the evidence (no failure dates or probabilities).
- [ ] Feature-specific checks:

## Implementation notes

_Codex: final endpoints and fields, components touched, deviations from the mock or fixtures and why, and test coverage._

## Change log

| Date | Change | By |
| --- | --- | --- |
| YYYY-MM-DD | Package created | Claude |
