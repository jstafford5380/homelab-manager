# Homelab Control web app

React, TypeScript, Vite and shadcn UI. See the [repository README](../../README.md) for Aspire development and browser-test setup.

## Structure and ownership

```text
src/
  app/          Application session, navigation, login, page composition
  features/     overview, storage, hardware, planning, settings, inventory,
                recovery, activity, findings
  components/   Shared controls and the shadcn ui/ primitives
  lib/          Domain-independent display preferences, clock and formatting
  assets/       Application assets
```

Feature folders own their pages, panels, forms, request hooks, contracts and styles. `App.tsx` composes pages and application state; it does not own feature forms or their API contracts. Routes retain their existing URLs and query parameters. Most pages load on demand; Planning remains eager so existing `?plan=` focus behavior does not depend on loading a route chunk.

Use direct imports rather than catch-all barrel files. Put shared domain contracts at the nearest feature ancestor, and never import a page just to access a type, validator, or status renderer. The application snapshot is defined in `app/overviewSnapshot.ts`; storage scan contracts are in `features/storage/scanModel.ts`; plan/review contracts live in `features/planning/planning.ts`. These remain explicit projections of the existing API, without adding a schema migration or changing JSON contracts.

Pages coordinate their feature and keep URL selection and focus ownership visible. Components own cohesive presentation or editing surfaces. Dedicated hooks such as `useAppSession`, `useStorageInventory`, `useHardwareData`, and `usePlanningReview` own their request lifecycles, including cancellation and retention of last-known data. Do not replace these different contracts with a configurable universal fetching hook.

`features/storage/legacy/` and `features/activity/legacy/` contain the older advanced Storage screens that are not reachable from the current application entry point or browser tests. Their code is retained because the associated APIs and feature history still exist; moving them is not a product decision to retire or re-enable those capabilities. New work should target the active pages, and reconnecting a legacy capability needs an explicit design decision. Shared shadcn primitives are a toolbox and may be unused until a feature needs them.

Keep tokens and common layout rules in `index.css`/`app/App.css`, with feature styles beside the feature. Some historical feature CSS remains in the application stylesheet; extracting it further requires visual checks of its shared selectors.

## Code checks

Install dependencies from the repository root with `npm run install:web`, then run:

```sh
npm run lint       # Oxlint across app source, browser tests and frontend config
npm run lint:fix   # Apply safe automatic lint fixes; review the resulting diff
npm run typecheck # TypeScript checks without bundling
npm run check:web # Lint followed by TypeScript checks (also used in CI)
```

Inside `src/Web`, the equivalent combined command is `npm run check`. `npm run build` still checks TypeScript before producing the Vite bundle.

[`.oxlintrc.json`](.oxlintrc.json) enables the React, TypeScript, Oxc and import plugins. Correctness rules, the Rules of Hooks and exhaustive Hook dependencies are errors, so they fail local checks and CI. Circular runtime imports are errors. React source files have a 500-code-line ceiling (blank/comment-only lines excluded); this is a backstop, not a target. Split by responsibilities before reaching it, and do not add blanket exceptions or ignore meaningful code just to fit. Browser scenarios and CSS are not component-size targets.

Intentionally unused function arguments may start with `_` (for example, the declarative `SelectOption` marker).

Existing React Compiler advisories (`react/purity`, `react/set-state-in-effect`) and Fast Refresh export advisories remain warnings. They are reported and are not hidden or counted as a clean warning-free run. Address them as affected components are maintained; promoting them to errors is a separate cleanup. The React Compiler itself is not enabled. Build output, dependency folders, coverage and browser-test artifacts are explicitly ignored.

[Frontend checks](../../.github/workflows/frontend-checks.yml) runs on pull requests and pushes to `main` and `development`, using Node 24 and the committed npm lockfile. This workflow does not change repository branch protection.

The repository recommends the official Oxc extension in VS Code-compatible editors. Oxlint includes a language server for editor integration; use the [official editor setup](https://oxc.rs/docs/guide/usage/linter/editors). Configuration and safe-fix behavior follow the [Oxlint documentation](https://oxc.rs/docs/guide/usage/linter/config-file-reference).
