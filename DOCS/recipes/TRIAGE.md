# TRIAGE — sort findings into the right tracker

**Trigger:** `TRIAGE [findings]` — paste raw observations or just the trigger if findings are already in chat.

**Gives you:** findings classified (blocker / fix / tuning / defer / cut) and routed to the right doc.

**When *not* to use it:** when the finding is a single obvious bug — just file it and move on.

---

## Where things live

| Doc | Holds |
|---|---|
| `WORKSPACE/RELEASE_V1.md` | **v1 scope** — what is in and out of 1.0, with status. Scope is locked: a new feature needs the user's explicit "yes, add to v1". |
| `WORKSPACE/PIPELINE.md` | **The live, ordered queue** — stubs, top item next; detail in `WORKSPACE/pipeline/items/<NN>-<slug>.md`. Adding an item: `WORKSPACE/pipeline/README.md` §"Working rules" (next unused number, never reused). |
| `WORKSPACE/BACKLOG.md` | v1.1+ and parking-lot ideas. |
| `WORKSPACE/bugs/discovered.md` | Bugs found incidentally during other work. |

## What I do

For each finding:

1. **Classify** — pick one:
   - **Critical blocker** / **v1 fix** → a `RELEASE_V1.md` entry in the right phase, **and** a `PIPELINE.md` stub if it is work to schedule
   - **Tuning** → `RELEASE_V1.md` (with a concrete value if known) and a PIPELINE stub if it needs a session
   - **Defer to v1.1** → `RELEASE_V1.md` "Deferred to v1.1 / Won't fix v1" or `BACKLOG.md`
   - **Won't-fix** → `RELEASE_V1.md` `[cut]` with reason
   - **Pending decision** → `RELEASE_V1.md` "Pending decisions", question explicit
   - **Off-scope** (not v1, not v1.1) → `BACKLOG.md`
   - **Incidental bug** → also `bugs/discovered.md`
2. **Don't reorder the queue on your own.** Where a new PIPELINE stub goes relative to the others is the manager's (or the user's) call; propose a position.
3. **Confirm** what was added/updated and where.

## Tips

- One finding can land in several places (RELEASE_V1 *and* PIPELINE *and* `discovered.md`). That's fine.
- If a finding is ambiguous about scope, file under "Pending decisions" with the question explicit. Don't silently assume v1 vs v1.1.
- Status changes count as TRIAGE work too — `[ ]` → `[T]` after a fix belongs here. A passing item is removed from RELEASE_V1 entirely, not ticked.
