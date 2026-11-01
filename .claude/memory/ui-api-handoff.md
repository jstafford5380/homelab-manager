---
name: ui-api-handoff
description: Workflow — implement UI with existing data, log API gaps in docs/UI_API_REQUESTS.md for Codex
metadata:
  type: feedback
---

When a UX decision needs data the API lacks: ship the UI using only existing data (never fake or infer it client-side), and add an entry to docs/UI_API_REQUESTS.md (screen, gap, suggested contract with nullability). Codex implements from that file; when an entry is done, wire up the UI and retire the entry.

**Why:** user confirmed (2026-11-01) this is the desired Claude(UI)/Codex(API) handoff; keeps domain/truthfulness logic in the API.
**How to apply:** check the file at session start for entries Codex completed; suggested shapes are proposals Codex may revise. See [[project-scope-and-roles]].
