# WORKSPACE

Living state for the project. Mutable. Frequently edited. The agent reads and updates these every session.

## What's here

| File | Purpose |
|---|---|
| [`RELEASE_V1.md`](RELEASE_V1.md) | **Source of truth** for v1 scope. Every item with a status. Updated continuously. |
| [`HOTBOARD.md`](HOTBOARD.md) | What's actively in motion right now. Capped at ~40 lines; oldest items rotate out. |
| [`BACKLOG.md`](BACKLOG.md) | Deferred ideas (`[ ]`/`[x]`/`[dropped]`). |
| [`DISCOVERIES.md`](DISCOVERIES.md) | Dated gotchas and insights from past sessions. |
| `bugs/discovered.md` | Bugs found incidentally during other work. |
| [`autotester_improvements.md`](autotester_improvements.md) | AUTOTEST harness friction backlog — consume in a focused improvement session. |
| `plans/` | In-progress plans (auto-archived on FINALIZE when their work ships). |
| `playtests/` | Raw playtest reports — historical, never edited after the session. |
| `archive/` | Historical: shipped plans, old session logs, old playtests. Periodically cleanable. |

## Conventions

- Update `RELEASE_V1.md` whenever a status changes — and commit when you do.
- `HOTBOARD.md` reflects "what I'm working on now". Stale items get rotated out.
- Session logs are not kept here — once finished, they go straight to `archive/sessions/` (see `DOCS/recipes/FINALIZE.md`).
- See `DOCS/modes/RELEASE.md` for the methodology.
