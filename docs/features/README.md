# Feature design handoff

New UI features and major redesigns start here as a design package. Claude and the owner design the experience as a working mock. Codex then implements both the UI and the backend that supports it. Because the design isn't tied to the current API, Codex can shape endpoints and components together instead of retrofitting one to the other.

## Roles

| Role | Owns |
| --- | --- |
| Owner | Product decisions, approving a package as `ready`, accepting the result |
| Claude | `docs/features/`: briefs, mocks, fixtures, assets, and post-implementation design reviews |
| Codex | All implementation in `src/Api` and `src/Web`, including the real API contract |

## Lifecycle

`exploring → (lock-in) → ready → in progress → implemented → reviewed → done`

1. **exploring:** Claude and the owner iterate on the design using a working mock only: a scratch file or a private published preview. **Nothing is written to `docs/features/` yet.** No brief, no fixtures, no package folder, no index entry. Rewriting Codex-facing docs on every iteration wastes effort, and half-settled docs risk being picked up early.
2. **lock-in:** the owner explicitly says the design is settled, e.g. "lock that feature in". Only then does Claude write the whole package in one pass: copy `_template/`, finalize the mock, write the brief and fixtures, and add the index row with status `ready`. Claude never treats approval of one detail, or a pause in feedback, as lock-in. When unsure, Claude asks.
3. **ready:** waiting for Codex. The package doesn't change after this unless the owner asks, and then every change gets a row in the brief's **Change log**. If a redesign is needed, go back to exploring outside the package and lock in again.
4. **in progress:** Codex is implementing. Questions and deviations go in the brief's **Implementation notes**.
5. **implemented:** merged, tested, and described in **Implementation notes**: final endpoints, components, and any deliberate differences from the mock.
6. **reviewed:** Claude has compared the running app against the mock in every state and width, and written `review.md`.
7. **done:** review findings are resolved or explicitly accepted by the owner.

Whoever changes the status updates it in the brief's header, with the date.

## Numbering

Numbers make it easy to point at work precisely, e.g. "implement 001.03" or "001.08 stays behind the SMART flag".

- **Package number (`001`).** Assigned when exploration starts, so the design conversation can use it before anything is written here. Claude puts it in the mock's title (e.g. "001 · Storage redesign") and keeps track of reserved numbers. The package folder and index row still appear only at lock-in. Numbers are never reused, even for an abandoned exploration.
- **Feature ID (`001.03`).** Each capability inside a package that could be built, flagged, deferred, or reviewed on its own gets an ID: package number, a dot, then a two-digit sequence. Claude assigns them during exploration and lists them in the brief's **Features** table at lock-in.
- **IDs are stable.** Never renumber. A dropped feature keeps its row, marked `dropped`. A new feature takes the next free number, even if it belongs between two others.
- **Use IDs everywhere** they help: acceptance checklist items, Codex's implementation notes, review findings, commit messages, and requests like "implement 001.01–001.04 now, defer 001.09".

## Package layout

At lock-in, copy `_template/` to `NNN-short-slug/`, using the package number reserved at exploration.

```
docs/features/NNN-short-slug/
  README.md   brief: goal, screens, states, copy, data needs, acceptance checklist, status
  mock.html   self-contained mock with a state switcher, built on the app's colour tokens
  fixtures/   sample data per state, in the proposed contract shape
  assets/     icons, images, anything generated for the design
  review.md   Claude's design review after implementation
```

## Rules that keep the handoff honest

- **The mock is the visual spec. The brief is the behavioural spec.** If they disagree, the brief wins. Note the conflict under **Implementation notes**.
- **Every state is designed, not just the happy path.** At minimum: normal, empty, loading, error or unavailable, stale or unknown data, and mobile width. The mock's state switcher must cover each one listed in the brief.
- **Fixtures are a proposal, not a binding contract.** Codex owns the real API shape and may change it. Record the final shape and the reason in **Implementation notes**.
- **Truthfulness rules apply to designs too.** No invented precision, failure predictions, or fake certainty in mock copy. Unknown, stale, and unsupported data must look different from healthy data.
- **Mocks use the app's tokens.** `_template/mock.html` carries a copy of the light and dark colour tokens from `src/Web/src/index.css`. Implementation should map to existing tokens and components, not copy mock CSS verbatim.
- **Small fixes don't need a package.** Copy tweaks, spacing, and bug fixes can go straight to Codex as normal tasks.

## Index

| # | Feature | Status | Updated |
| --- | --- | --- | --- |
| [001](001-storage-redesign/README.md) | Storage redesign | ready | 2026-11-01 |
