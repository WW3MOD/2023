# FINALIZE — session wrap-up

**Trigger:** `FINALIZE` after completing a feature, fix, or meaningful chunk of work.

**Gives you:** a clean session end — bell rung, tracker updated, session promoted, everything committed. Future-me (and the user) can pick up cleanly next session.

**When *not* to use it:** in the middle of work, on micro-edits, or when changes are still uncommitted-WIP that the user wants to inspect first.

---

## What I do

1. `printf "\a"` — ring the terminal bell so the user knows.
2. **Double-check against `WORKSPACE/DISCOVERIES.md`** — ensure nothing was violated. Add a new entry if the session uncovered a gotcha worth remembering.
3. **Update `WORKSPACE/RELEASE_V1.md`** — flip statuses for items touched (e.g. `[~]` → `[T]` or `[x]`); move shipped items to "Recently completed".
4. **Update `WORKSPACE/HOTBOARD.md`** — refresh "Working on" and recent wins. Keep under 40 lines (rotate oldest items out).
5. **Archive completed plans** — for each `WORKSPACE/plans/*.md` whose work this session shipped:
   - If the corresponding tracker item is now `[x]` or `[T]`, `git mv` the plan to `WORKSPACE/archive/plans/`.
   - If the plan was a brainstorm-handoff that's been resolved, archive it too.
   - Tracker entries can keep referencing the archive path; don't break links.
6. **Promote session file** (if any): rename `WORKSPACE/archive/sessions/active_*.md` → `WORKSPACE/archive/sessions/<YYMMDD>_<topic>.md` (sessions live in archive directly — they're historical records, not active state).
7. **Update `WORKSPACE/BACKLOG.md`** — add deferred items, mark completed with `[x]`.
8. **Auto-commit** all changes with a descriptive message (no separate FINALIZE-only commit if everything is already committed).
9. **Review CLAUDE.md** — new pattern worth documenting? Structural change? Recurring gotcha? Update if yes. New recipe emerged? Add to `DOCS/recipes/`.
10. **PITFALL anchors check** — did I add any `// PITFALL:` (or `# PITFALL:`) comments this session? `git grep PITFALL` against touched files, sanity-check placement and wording, mention them in the wrap so the user can review. An outdated or wrong PITFALL is worse than none.

## Tips

- If the session was small / single-file, you don't need every step — skip whatever doesn't apply. The point is "leave the workspace tidy", not "ritual".
- If a NEW recipe or pattern emerged that recurs, add it to `DOCS/recipes/` (and add a row to `DOCS/recipes/README.md`) before commit.
