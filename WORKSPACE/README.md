# WORKSPACE

Living state for the project. Mutable. Frequently edited. The agent reads and updates these every session.

## Core files

| File | Purpose |
|---|---|
| [`RELEASE_V1.md`](RELEASE_V1.md) | **Source of truth** for v1 scope. Every item with a status. Updated continuously. |
| [`HOTBOARD.md`](HOTBOARD.md) | What's actively in motion right now. Capped at ~40 lines; oldest items rotate out. |
| [`BACKLOG.md`](BACKLOG.md) | Deferred ideas (`[ ]`/`[x]`/`[dropped]`). |
| [`DISCOVERIES.md`](DISCOVERIES.md) | Dated gotchas and insights from past sessions. |
| `bugs/discovered.md` | Bugs found incidentally during other work. |

## Folders

| Folder | Purpose |
|---|---|
| `plans/` | In-progress plans only — archived to `archive/plans/` when their work ships. |
| `playtests/` | Raw playtest reports — historical, never edited after the session. |
| `ai/` | AI overhaul workspace — problem statement, substrate design, phase tracking (older stage docs in `ai/archive/`). |
| `cohesion/` | Grouped-unit movement workspace — density/perception layer, design directions. |
| `automation/` | Automation workflow plan (focus-steal fix, test lanes, autonomous queue, …). |
| `lobby/` | Lobby redesign workspace — decisions, implementation plan, mockups. Implementation shipped; kept as design record. |
| `archive/` | Historical: `archive/plans/` (shipped plans), `archive/sessions/` (finished session logs), `archive/playtests/`. Periodically cleanable. |

## Conventions

- Update `RELEASE_V1.md` whenever a status changes — and commit when you do.
- `HOTBOARD.md` reflects "what I'm working on now". Stale items get rotated out.
- `DISCOVERIES.md` entries are always dated, with code refs where possible. Curation passes verify and promote entries into `DOCS/reference/` (tagging `[promoted]`/`[rejected: reason]`) — see `DOCS/reference/README.md`.
- Playtest reports are raw and historical — never edit a past report; TRIAGE updates `RELEASE_V1.md`.
- Session logs: `archive/sessions/active_<YYMMDD_HHMM>_<topic>.md` while running, promoted to `archive/sessions/<YYMMDD>_<topic>.md` on FINALIZE — sessions are historical the moment they finish.
- No duplication between WORKSPACE/ files and auto-memory (`.claude/projects/`).
- See `DOCS/modes/RELEASE.md` for the methodology.
