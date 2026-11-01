---
name: design-scope-backend-choices
description: In design packages, leave collection/access-method choices to Codex and design the UI around capabilities
metadata:
  type: feedback
---

When a design depends on how data is collected (e.g. Synology via DSM API vs SNMP), don't push the owner to decide. Codex works out the access method. The design should make the UI support whatever each platform can supply. The owner will design the UX for unsupported or unavailable metrics separately, later. Until then, treat any "not measured" treatment in a mock as a placeholder.

**Why:** owner, 2026-11-01, on package 002 Hardware: "dont worry about that for now, codex will work it out, just try to make the UI support what it can and I'll work out the UX for unsupported later".
**How to apply:** at lock-in, put the access method under the brief's Data needs as "Codex's choice", not under Open questions. Mark unsupported-metric states as placeholder/owner-owned rather than final copy. Related: [[feature-design-handoff]], [[project-scope-and-roles]].
