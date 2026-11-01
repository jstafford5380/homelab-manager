---
name: project-scope-and-roles
description: What Homelab Control is for, and the Claude (UI/UX) vs Codex (API/core logic) split
metadata:
  type: project
---

Homelab Control is a single pane of glass for the owner's home network servers (TrueNAS NAS + standalone servers). Goal goes beyond aggregate health: help the owner stay ahead of building issues — e.g. drives nearing EoL (rising read/write errors, reallocated/pending sectors), and when to plan buying drives, adding RAM, upgrading board/CPU — so upkeep is proactive, not reactive.

Division of labor (stated 2026-11-01): Claude owns UI/UX design and implementation (src/Web). Codex owns core coding of the API and supplementary logic (src/Api, .NET 10 + Aspire + EF/SQLite).

**Why:** avoid stepping on Codex's work; Claude's value here is design quality.
**How to apply:** default to working in src/Web; when UI needs an API change, describe the contract needed rather than implementing backend logic, unless the user asks. See [[ui-stack-and-constraints]].
