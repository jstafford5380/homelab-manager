# Agent notes

## Ownership

- **Codex** owns all implementation: `src/Api` (ASP.NET, Aspire, EF/SQLite, collectors, domain rules) and `src/Web` (React UI), including the real API contracts.
- **Claude** owns design: feature packages in `docs/features/` (briefs, mocks, fixtures, assets), plus design reviews of implemented features.
- The owner approves packages and accepts results.

## Local development

Use shadcn UI components whenever available, including Select for dropdown selectors and Tooltip for hover/focus information. Add missing shadcn components under `src/Web/src/components/ui`; avoid native select controls and HTML title tooltips in application UI.

When the local API needs a restart, restart the `api` resource through Aspire. Use `dotnet dnx --yes aspire.cli@13.6.4 -- resource api restart --apphost src/Api/Homelab.AppHost/Homelab.AppHost.csproj --non-interactive` with the running AppHost.

## Implementing a feature package

Packages live in `docs/features/NNN-slug/`. The process is in [docs/features/README.md](docs/features/README.md). When asked to implement a feature, or to "pick up the next feature":

1. Unless told which one, take the lowest-numbered package whose brief has status `ready`. Set it to `in progress` with today's date, in the brief and in the index table in `docs/features/README.md`.
2. The brief (`README.md`) is the behavioural spec. `mock.html` is the visual spec: open it and step through every option in its State, Width and Theme switchers. If the two disagree, the brief wins. To open mocks by URL, run `npm run gallery` and visit `http://127.0.0.1:4178/NNN-slug/mock.html`. Toolbar settings can be passed as a query string, e.g. `?state=stale&width=mobile&theme=dark`.
3. `fixtures/` shows the data each state needs, as a proposal. Design the real API shape for correctness and performance, and keep the project's read-only NAS contract and truthfulness rules: unknowns stay explicit, and no failure predictions.
4. Build the UI with the app's existing tokens and components (`src/Web/src/index.css`, `src/Web/src/components/ui`). Don't paste mock CSS. Map the mock's `@container app` rules to the app's `@media` breakpoints (700px and 1100px).
5. Work through the brief's acceptance checklist, and extend `src/Web/e2e` to cover each state, including axe checks in light and dark.
6. As each feature ID is merged and tested, set its status in the **Features** table and fill in its entry under **Implementation notes**: final endpoints and fields, components touched, deviations from the mock or fixtures and why, and test coverage. When the package is complete, set its status to `implemented`. Claude reviews next.
7. If a `review.md` exists with open findings, fix them (or note why not), then record each resolution in that file.

Don't edit mocks, fixtures or design copy to match the implementation. Record the difference in **Implementation notes** instead.

### Package numbers and feature IDs

- A package number (`001`) names a whole package. A feature ID (`001.03`) names one capability, listed in the brief's **Features** table. See **Numbering** in [docs/features/README.md](docs/features/README.md).
- When asked to implement specific IDs (e.g. "implement 001.01–001.04"), do only those, plus anything they list under **Depends on**. Update each feature's **Status** in the table as it lands, and write its entry under **Implementation notes** using the ID as the heading.
- A feature with a **Flag** must be completely absent from the UI while the flag is off: no placeholders, no "coming soon". Turn the flag on only when its backend support is merged and tested, and say so in that feature's implementation notes.
- Reference feature IDs in commit messages and review responses.
- Set the package status to `implemented` only when every feature that isn't `deferred` or `dropped` is `implemented`.

## Legacy UI → API requests

[docs/UI_API_REQUESTS.md](docs/UI_API_REQUESTS.md) predates feature packages. Finish its remaining open entries using the rules in that file. New work arrives as feature packages instead.
