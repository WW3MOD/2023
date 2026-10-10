# FINALIZE — session wrap-up

**Trigger:** `FINALIZE` after completing a feature, fix, or meaningful chunk of work.

**Gives you:** a clean session end — bell rung, trackers updated, everything committed. Future-me (and the user) can pick up cleanly next session.

**When *not* to use it:** in the middle of work, on micro-edits, or when changes are still uncommitted WIP the user wants to inspect first.

---

## What I do

1. `printf "\a"` — ring the terminal bell.
2. **Check against `WORKSPACE/DISCOVERIES.md`** — nothing violated? Add a dated entry (with code refs) if the session uncovered a gotcha worth remembering. New knowledge goes there, not straight into `DOCS/reference/` or CLAUDE.md (CLAUDE.md §Knowledge bank).
3. **Update `WORKSPACE/RELEASE_V1.md`** — move statuses for items touched along its legend (`[ ]` → `[~]` → `[T]`). An item that has **passed** AUTOTEST or playtest is **removed entirely**: the file keeps no `[x]` entries and no "Recently completed" section; commit history is the archive.
4. **Update `WORKSPACE/PIPELINE.md`** if a queue item moved — close a finished item per `WORKSPACE/pipeline/README.md` §"Working rules" (dossier into `pipeline/archive/closed-items.md`, a line in `archive/shipped-log.md` if it shipped code, stub deleted).
5. **Update `WORKSPACE/HOTBOARD.md`** — refresh what is in motion. Cap ~40 lines; rotate shipped items out. Every line carries a `file:line` or a SHA.
6. **Archive completed plans** — `git mv` each `WORKSPACE/plans/*.md` whose work shipped (or whose brainstorm resolved) to `WORKSPACE/archive/plans/`. Links may keep pointing at the archive path.
7. **Update `WORKSPACE/BACKLOG.md`** — add deferred items; mark completed ones `[x]` (BACKLOG's own convention, unlike RELEASE_V1).
8. **Commit** with a descriptive message (skip if everything is already committed). Commit only; pushing follows CLAUDE.md's push rule.
9. **New recipe?** If a recurring workflow emerged, add it to `DOCS/recipes/` and a row to `DOCS/recipes/README.md`.
10. **PITFALL anchors check** — did I add any `// PITFALL:` (or `# PITFALL:`) comments this session? `git grep PITFALL` against touched files, sanity-check placement and wording, mention them in the wrap so the user can review. An outdated or wrong PITFALL is worse than none.

## Tips

- If the session was small / single-file, skip whatever doesn't apply. The point is "leave the workspace tidy", not ritual.
