# RELEASE — v1 release mode (default)

**Trigger:** `RELEASE` to enter or re-affirm release mode. This is the **default** mode — assume RELEASE unless the user has explicitly switched to EXPERIMENTAL.

**Gives you:** a methodology for working toward v1. Scope is locked, work is phase-driven, every change touches the tracker, every commit moves a status.

**When *not* to use it:** when prototyping or exploring ideas that aren't committed to v1 — switch to EXPERIMENTAL instead.

---

## Source of truth

`WORKSPACE/RELEASE_V1.md` — every v1 item with a status. Update continuously as items change state. Nothing enters v1 without showing up here.

## Scope is locked

New features need an explicit "yes, add to v1" from the user. Ideas raised during work go to:
- `RELEASE_V1.md` → "Pending decisions" — until triaged
- `WORKSPACE/BACKLOG.md` — if clearly v1.1 or later

## Phases (rough order, not strict)

- **Phase A — Stabilize**: verify everything currently "needs playtesting"
- **Phase B — Tier-1 fixes**: bugs and gameplay gaps that block release
- **Phase C — Polish**: sounds, icons, descriptions, polish threads

## Status legend

`[ ]` open · `[~]` in-progress · `[T]` testing · `[x]` done · `[v1.1]` deferred · `[cut]` won't-fix v1

## The loop

`PLAYTEST → user plays → user reports → TRIAGE → fix → repeat`. Most sessions sit somewhere in this cycle.

## Status snapshot (when asked)

When the user wants a quick read on release health:
1. Read `WORKSPACE/RELEASE_V1.md`, last ~10 commits.
2. Print: current phase, count by status (open / in-progress / testing / done), top blockers, anything drifting (untouched recently, or sitting in `[T]` for too long).

## Commit cadence

- Frequent small commits over batched changes — descriptive messages.
- **Behavioral fixes ship with their auto-asserting test** (RED → GREEN proof). The test scenario lives in `mods/ww3mod/maps/test-*/` and gets committed alongside the fix. See `DOCS/skills/AUTOTEST.md` for the loop and the "default-to-AUTOTEST" checklist.
- **Recurring traps get a `// PITFALL:` anchor.** If the bug's root cause is a non-obvious foot-gun a future reader would also hit, leave a one-line PITFALL at the temptation site. Same commit as the fix. See CLAUDE.md "PITFALL Comments".
- Test scenario + fix + tracker update belong in one commit (so a fix can't ship without its regression test).
- Always commit before ending a session. Never leave uncommitted changes behind.
- Never push to remote — the user pushes manually.
