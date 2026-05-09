# TRIAGE — sort findings into v1 buckets

**Trigger:** `TRIAGE [findings]` — paste raw observations or just the trigger if findings are already in chat.

**Gives you:** organized v1 tracker. Findings get classified (blocker / fix / tuning / defer / cut) and routed to the right doc.

**When *not* to use it:** when the finding is a single obvious bug — just file it and move on.

---

## What I do

For each finding:

1. **Classify** — pick one:
   - **Critical blocker** → `RELEASE_V1.md` Phase A or B
   - **v1 fix** → `RELEASE_V1.md` Phase B or C
   - **Tuning** → `RELEASE_V1.md` (with concrete value if known)
   - **Defer to v1.1** → `RELEASE_V1.md` "Deferred" or `CLAUDE/BACKLOG.md`
   - **Won't-fix** → `RELEASE_V1.md` `[cut]` with reason
   - **Pending decision** → `RELEASE_V1.md` "Pending decisions"
2. **Route**:
   - In-scope items → `RELEASE_V1.md` (new entry, status update, or "Pending decisions")
   - Off-scope (not v1, not v1.1) → `CLAUDE/BACKLOG.md`
   - Bugs found incidentally during other work → also append to `CLAUDE/bugs/discovered.md`
3. **Confirm** what was added/updated and where, in the end-of-message block.

## Tips

- One finding can land in multiple places (in v1 *and* in `discovered.md`). That's fine — the tracker is the source of truth, the others are for spotting patterns.
- If a finding is ambiguous about scope, file under "Pending decisions" with the question explicit. Don't silently assume v1 vs v1.1.
- Status changes count as TRIAGE work too — flipping `[ ]` → `[T]` after fixing belongs here, not just on initial filing.
