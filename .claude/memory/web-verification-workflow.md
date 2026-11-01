---
name: web-verification-workflow
description: How to visually and e2e-verify src/Web changes without disturbing the user's running Aspire stack
metadata:
  type: project
---

- Playwright e2e (`npx playwright test` in src/Web) starts the API via scripts/run-e2e.py with `--no-build`, and serves the SPA from src/Api/Homelab.Api/wwwroot. That folder only refreshes when the API project builds (CopyWebAssets copies Web/dist). So: `vite build`, then `dotnet build src/Api/Homelab.Api -o <scratch dir>` (refreshes wwwroot without overwriting bin/ the user's Aspire run uses), then run Playwright.
- The user usually has Aspire running (Vite on a random port proxying the API); their real owner credentials are unknown — don't use that instance for testing.
- For screenshots with realistic data, a Playwright script that mocks /api/v1/session, /overview and /planning/review against a Vite dev server works well; set theme via localStorage key `homelab.appearance` (appearance.ts overrides data-theme).
- Use this workflow for design reviews of Codex implementations (see [[feature-design-handoff]]).

Related: [[ui-stack-and-constraints]]
