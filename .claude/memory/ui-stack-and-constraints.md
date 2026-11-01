---
name: ui-stack-and-constraints
description: Frontend stack facts and standing UX constraints for src/Web
metadata:
  type: project
---

Stack: React 19 + Vite 8 + TypeScript 7 + Tailwind 4 + shadcn (new-york, slate) + radix-ui + lucide-react + react-router 8. Playwright e2e (with axe) in src/Web/e2e; lint via oxlint. Root package.json has dev:web / build:web / test:web / lint scripts.

Standing constraints from project docs (IMPLEMENTATION_STATUS.md): owner wants intuitive, useful, beautiful, consistent UX; every UI increment should include responsive (mobile) visual review plus keyboard/accessibility checks in light and dark. The app is monitor-and-advise only — no NAS mutation/maintenance actions. Health display must be truthful: show unknown/unsupported/stale explicitly, never fabricate or imply causality/failure probability. No fabricated production data in the frontend.

**How to apply:** respect these when designing screens; related: [[project-scope-and-roles]].
