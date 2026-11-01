---
name: feature-design-handoff
description: How design packages flow to Codex via docs/features (lifecycle, template, review duty)
metadata:
  type: feedback
---

New features/major redesigns: copy docs/features/_template to docs/features/NNN-slug, iterate brief + mock with the owner (status draft), owner approves → ready, Codex implements → implemented, Claude reviews the running app against the mock in every state/width/theme and writes review.md → reviewed → done. Keep the index table in docs/features/README.md current. AGENTS.md tells Codex how to pick packages up.

Mock template notes: tokens are generated from src/Web/src/index.css (regenerate if it changes); toolbar switches state/width/theme; responsive rules must use `@container app` (not @media) so the width switcher works; inline fixtures mirror fixtures/*.json. Also publish the mock as an Artifact for the owner to view/share when useful.

Legacy docs/UI_API_REQUESTS.md: Codex finishes open entries there; no new entries. UR-1 (capacityOutlook on /overview) was done by Codex 2026-11-01 but not yet surfaced in UI — candidate first package.

**Why:** owner set this up 2026-11-01 to get the best results from the Claude-designs / Codex-implements split.
**How to apply:** at session start, check docs/features/README.md index for packages in `implemented` (need review) and the request file for done entries. See [[project-scope-and-roles]].
