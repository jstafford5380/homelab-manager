---
name: feature-design-handoff
description: How design packages flow to Codex via docs/features — explore mock-only, write docs only on explicit "lock it in"
metadata:
  type: feedback
---

New features/major redesigns: EXPLORE with a mock only (scratchpad file and/or private published Artifact) — do NOT create or edit anything in docs/features/ (no folder, brief, fixtures, index row) while iterating. Only when the owner explicitly locks it in ("lock that feature in" or similar) write the full package in one pass: copy docs/features/_template to docs/features/NNN-slug, finalize mock, brief, fixtures, index row with status ready. Approval of one detail or silence is not lock-in; if unsure, ask. After ready, change the package only when the owner asks (log it in Change log). Then Codex implements → implemented, Claude reviews the running app against the mock in every state/width/theme and writes review.md → reviewed → done. Keep the index table in docs/features/README.md current. AGENTS.md tells Codex how to pick packages up.

Numbering: reserve the package number (NNN) when exploration starts — put it in the canvas/mock title, record it below — and assign feature IDs (NNN.01, NNN.02…) during exploration (a sticky note on the canvas works). IDs are stable: never renumber, mark dropped, next free number for additions. At lock-in they become the brief's Features table (with Depends on / Flag / Status). Reserved: 001 = Storage redesign — LOCKED IN 2026-11-01, package docs/features/001-storage-redesign (status ready; canvas https://claude.ai/artifact/DvJnB3bpZr8whJFi7iNccp; IDs 001.01–001.20, 001.17–001.20 behind flag `smart`). Next step for Claude: review once Codex marks it implemented. Next free: 002.

Gallery: `npm run gallery` (scripts/gallery.mjs) serves docs/features at http://127.0.0.1:4178/; mocks take ?state=Mock template notes:width=Mock template notes:theme=Mock template notes:smart= presets — use it for reviews and to give Codex URLs.

Mock template notes: tokens are generated from src/Web/src/index.css (regenerate if it changes); toolbar switches state/width/theme; responsive rules must use `@container app` (not @media) so the width switcher works; inline fixtures mirror fixtures/*.json. Also publish the mock as an Artifact for the owner to view/share when useful.

Legacy docs/UI_API_REQUESTS.md: Codex finishes open entries there; no new entries. UR-1 (capacityOutlook on /overview) was done by Codex 2026-11-01 but not yet surfaced in UI — candidate first package.

**Why:** owner set this up 2026-11-01 to get the best results from the Claude-designs / Codex-implements split; the lock-in gate avoids burning tokens rewriting Codex-facing docs on every design iteration.
**How to apply:** at session start, check docs/features/README.md index for packages in `implemented` (need review) and the request file for done entries. See [[project-scope-and-roles]].
