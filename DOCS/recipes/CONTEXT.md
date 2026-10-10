# CONTEXT — quick orientation on an area of the codebase

**Trigger:** `CONTEXT <area>` — e.g. `CONTEXT garrison`, `CONTEXT supply economy`, `CONTEXT artillery`.

**Gives you:** a tight summary of the current state of an area in 1–2 minutes instead of 10–15 minutes of grepping. Recent commits, open work, known issues, file pointers.

**When *not* to use it:** when working on the same area as the previous session — context is already warm.

---

## What I do

1. **Check the tree is current**: `git status -sb` and `git rev-list --count HEAD..@{u}`. If behind, say so and scope the summary to the ref actually read.
2. **What is in motion right now** — `WORKSPACE/HOTBOARD.md` for the area, and `WORKSPACE/PIPELINE.md` stubs touching it (each links its dossier under `WORKSPACE/pipeline/items/`; read only the matching ones). For other agents working in parallel, `git worktree list` and `git branch --no-merged main`.
3. **`git log --oneline --since="4 weeks ago" -- <relevant-paths>`** — recent activity on the files.
4. **Scope status** — `WORKSPACE/RELEASE_V1.md` entries for the area (`[T]`, `[~]`, `[ ]`). That file is the v1 scope list, not the live queue, and its own header says which parts are stale.
5. **Known traps and bugs** — `WORKSPACE/DISCOVERIES.md` (dated entries), `WORKSPACE/bugs/discovered.md`, and the curated `DOCS/reference/` section the CLAUDE.md routing table points to for the area. Closed work and its rulings: `WORKSPACE/pipeline/archive/closed-items.md`.
6. **Plans** — `WORKSPACE/plans/` for active plan docs; `WORKSPACE/archive/plans/` for finished ones.
7. **Print a tight summary**:
   - **Current state** — what's working, what's pending playtest, what's broken
   - **Recent activity** — last 3–5 commits, what changed
   - **Open work / known issues** — the PIPELINE items, RELEASE_V1 entries and bugs found above
   - **Files to know** — 3–6 most relevant source files
   - **Ref read** — branch and short SHA

Keep the summary readable in under a minute — this is a starter, not a deep dive.

## Tips

- If the area term is ambiguous ("supply" could mean supply trucks, Logistics Centers, ammo, or supply caches), ask the user to narrow before grepping.
- Don't paste full file contents — pointers and one-liners only.
- If recent activity is light, say so — knowing nothing-recent-happened is itself useful.
