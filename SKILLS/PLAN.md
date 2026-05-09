# PLAN — design a feature or change before implementing

**Trigger:** `PLAN <feature or change>` (or just `PLAN` to plan whatever's currently being discussed).

**Gives you:** alignment on approach before code is written. I research, ask questions until the plan is sufficient, write a durable plan doc, then wait for approval.

**When *not* to use it:** small bug fixes, single-file edits, anything where the goal is obvious. Just do those directly.

---

## What I do

1. **Ask clarifying questions** — keep asking until the user says the plan is sufficient. Better to over-ask than to write a plan against the wrong assumptions.
2. **Research** — read relevant code, grep for patterns, check existing systems and prior art. Use Explore agents for broad surveys when the surface area is large.
3. **Write the plan** to `CLAUDE/plans/<YYMMDD>_<topic>.md`:
   - **Goal** — one paragraph
   - **Constraints** — what must not break, what's locked
   - **Affected files** — list with brief role
   - **Step-by-step implementation** — ordered, each step independently buildable
   - **Risks / open questions** — what could go wrong, what's still unclear
4. **Summarize in chat** — short version of the plan, point to the doc for detail.
5. **Wait for approval** before implementing.

## Tips

- If the plan would be longer than the implementation, you don't need a plan — just code it.
- If the plan reveals the user wants something different than I assumed, that's a win — saved a refactor.
- Plans rot. Update the plan doc as the implementation reveals new constraints, then commit the update.
