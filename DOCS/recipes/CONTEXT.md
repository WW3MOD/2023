# CONTEXT — quick orientation on an area of the codebase

**Trigger:** `CONTEXT <area>` — e.g. `CONTEXT garrison`, `CONTEXT supply economy`, `CONTEXT artillery`.

**Gives you:** a tight summary of the current state of an area in 1–2 minutes instead of 10–15 minutes of grepping. Recent commits, open work, known issues, file pointers.

**When *not* to use it:** when working on the same area as the previous session — context is already warm.

---

## What I do

1. **Search `WORKSPACE/RELEASE_V1.md`** for items touching the area — `[T]`, `[~]`, `[ ]` related to it.
2. **`git log --oneline --since="4 weeks ago" -- <relevant-paths>`** — recent activity on the files.
3. **Search `WORKSPACE/DISCOVERIES.md`** for dated entries on the topic.
4. **Search `WORKSPACE/plans/`** for any plan docs touching the area.
5. **Check `WORKSPACE/archive/sessions/active_*.md`** — anything in flight by another agent on this area.
6. **Print a tight summary**:
   - **Current state** — what's working, what's pending playtest, what's broken
   - **Recent activity** — last 3–5 commits, what changed
   - **Open work / known issues** — relevant `RELEASE_V1.md` entries
   - **Files to know** — 3–6 most relevant source files

Keep the summary readable in under a minute — this is a starter, not a deep dive.

## Tips

- If the area term is ambiguous ("supply" could mean supply trucks, Logistics Centers, ammo, or supply caches), ask the user to narrow before grepping.
- Don't paste full file contents — pointers and one-liners only.
- If recent activity is light, say so — knowing nothing-recent-happened is itself useful.
