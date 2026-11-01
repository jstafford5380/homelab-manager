---
name: smart-ssh-decision
description: Owner chose SSH+smartctl for SMART data, server-side locked to read-only; UI must feature-flag SMART until the adapter exists
metadata:
  type: project
---

2026-11-01: Owner decided SMART data will come from the backend running smartctl over SSH (via existing tunnel), overriding the "no shell" rule. Agreed boundary: dedicated key-only user, forced-command wrapper allowing only `smartctl --scan` and `smartctl --json -x -n standby /dev/<disk>`, narrow sudo (TrueNAS "allowed sudo commands"), no PTY/forwarding, 6–12h schedule, never start self-tests. Codex implements and updates NAS_READ_MANIFEST/README safety docs.

Owner also wants SMART-derived UI (hours, counters, self-test, trend, firmware, Import SMART button) behind a feature flag until the adapter ships; without it, drive health comes from ZFS member state, ZFS error counters, NAS alerts and cached temps.

**Why:** TrueNAS API SMART methods were deliberately excluded; SSH works across TrueNAS/Proxmox/Linux for the multi-server goal.
**How to apply:** include both in the storage package brief at lock-in (Data needs + acceptance checklist); the storage mock has a `smartEnabled` tweak showing both states. A per-server "Health data" setup screen is a likely later package. See [[feature-design-handoff]].
