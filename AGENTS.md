# Agent notes

## Ownership

- `src/Api` (ASP.NET, Aspire, EF/SQLite, collectors, domain rules) is owned by Codex.
- `src/Web` (React UI/UX) is owned by Claude. Avoid UI changes unless a task requires them; describe needed UI follow-ups in the request file below instead.

## UI → API requests

UI work logs the API data it needs in [docs/UI_API_REQUESTS.md](docs/UI_API_REQUESTS.md). When asked to "pick up a UI request", or when starting API work:

1. Read the **Open** entries. Unless told which one, take the lowest-numbered entry whose status is `requested`, and set it to `in progress` with today's date.
2. Treat the suggested contract as a proposal. Keep the project's read-only NAS contract and truthfulness rules (unknowns stay explicit; no failure predictions). If you change the shape or decline part of the request, explain why under **Notes**.
3. When it's merged and tested, set the status to `done`, record the final endpoint and fields under **Implemented as**, and leave the entry in **Open**. The UI side wires it up and moves it to **Completed**.
