# FINALIZE — session wrap-up

**Trigger:** `FINALIZE` after completing a feature, fix, or meaningful chunk of work.

**Gives you:** a clean session end — bell rung, tracker updated, session promoted, everything committed. Future-me (and the user) can pick up cleanly next session.

**When *not* to use it:** in the middle of work, on micro-edits, or when changes are still uncommitted-WIP that the user wants to inspect first.

---

## What I do

1. `printf "\a"` — ring the terminal bell so the user knows.
2. **Double-check against `CLAUDE/DISCOVERIES.md`** — ensure nothing was violated. Add a new entry if the session uncovered a gotcha worth remembering.
3. **Update `CLAUDE/RELEASE_V1.md`** — flip statuses for items touched (e.g. `[~]` → `[T]` or `[x]`); move shipped items to "Recently completed".
4. **Update `CLAUDE/HOTBOARD.md`** — refresh "Working on" and recent wins. Keep under 40 lines (rotate oldest items out).
5. **Promote session file** (if any): rename `CLAUDE/sessions/active_*.md` → `CLAUDE/sessions/<YYMMDD>_<topic>.md`.
6. **Update `CLAUDE/BACKLOG.md`** — add deferred items, mark completed with `[x]`.
7. **Auto-commit** all changes with a descriptive message (no separate FINALIZE-only commit if everything is already committed).
8. **Review CLAUDE.md** — new pattern worth documenting? Structural change? Recurring gotcha? Update if yes.

## Tips

- If the session was small / single-file, you don't need every step — skip whatever doesn't apply. The point is "leave the workspace tidy", not "ritual".
- If a NEW skill or pattern emerged that recurs, add it to `SKILLS/` (and add a row to `SKILLS/README.md`) before commit.
