---
name: project-scope-and-roles
description: What Homelab Control is for, and the Claude (design) vs Codex (all implementation) split
metadata:
  type: project
---

Homelab Control is a single pane of glass for the owner's home network servers (TrueNAS NAS + standalone servers). Goal goes beyond aggregate health: help the owner stay ahead of building issues — e.g. drives nearing EoL (rising read/write errors, reallocated/pending sectors), and when to plan buying drives, adding RAM, upgrading board/CPU — so upkeep is proactive, not reactive.

Platforms (owner, 2026-11-01): only TrueNAS and Synology matter "for now and the foreseeable future". Don't design for Proxmox, generic Linux or BMC/Redfish; use Synology as the second-platform example in mocks. Hardware metrics plan: inventory from existing reads (system.info) now; TrueNAS reporting API and a Synology collector behind flag `hwmetrics`; manual readings for everything else (e.g. power draw).

Division of labor (revised 2026-11-01): Claude owns design — feature packages in docs/features/ (brief, mock.html, fixtures, assets) and post-implementation design reviews. Codex owns ALL implementation, src/Api and src/Web, including real API contracts. The overview redesign (commit 20af51f) was the last UI Claude implemented directly.

**Why:** owner wants Codex to optimize UI + backend together instead of adapting to Claude's implementation; mirrors their earlier "download mock HTML, Codex implements" workflow.
**How to apply:** don't edit src/Web or src/Api unless the owner explicitly asks; produce packages per [[feature-design-handoff]]. Constraints in [[ui-stack-and-constraints]] still govern designs.
