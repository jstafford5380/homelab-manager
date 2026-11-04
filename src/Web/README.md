# Homelab Control web app

React, TypeScript, Vite and shadcn UI. See the [repository README](../../README.md) for Aspire development and browser-test setup.

## Code checks

Install dependencies from the repository root with `npm run install:web`, then run:

```sh
npm run lint       # Oxlint across app source, browser tests and frontend config
npm run lint:fix   # Apply safe automatic lint fixes; review the resulting diff
npm run typecheck # TypeScript checks without bundling
npm run check:web # Lint followed by TypeScript checks (also used in CI)
```

Inside `src/Web`, the equivalent combined command is `npm run check`. `npm run build` still checks TypeScript before producing the Vite bundle.

[`.oxlintrc.json`](.oxlintrc.json) enables the React, TypeScript and Oxc plugins. Correctness rules, the Rules of Hooks and exhaustive Hook dependencies are errors, so they fail local checks and CI. Intentionally unused function arguments may start with `_` (for example, the declarative `SelectOption` marker).

Existing React Compiler advisories (`react/purity`, `react/set-state-in-effect`) and Fast Refresh export advisories remain warnings. They are reported and are not hidden or counted as a clean warning-free run. Address them as affected components are maintained; promoting them to errors is a separate cleanup. The React Compiler itself is not enabled. Build output, dependency folders, coverage and browser-test artifacts are explicitly ignored.

[Frontend checks](../../.github/workflows/frontend-checks.yml) runs on pull requests and pushes to `main` and `development`, using Node 24 and the committed npm lockfile. This workflow does not change repository branch protection.

The repository recommends the official Oxc extension in VS Code-compatible editors. Oxlint includes a language server for editor integration; use the [official editor setup](https://oxc.rs/docs/guide/usage/linter/editors). Configuration and safe-fix behavior follow the [Oxlint documentation](https://oxc.rs/docs/guide/usage/linter/config-file-reference).
