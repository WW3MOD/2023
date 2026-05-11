# DOCS

Static project documentation. Three kinds:

- [`modes/`](modes/) — operating modes (RELEASE, EXPERIMENTAL). One in effect at a time; sets the stance for the session. Index in [`modes/README.md`](modes/README.md).
- [`recipes/`](recipes/) — workflow triggers (AUTOTEST, PLAN, DEMO, …). The agent follows these when the user types the trigger phrase. Index in [`recipes/README.md`](recipes/README.md).
- [`reference/`](reference/) — system architecture, balance, project assessment. Read on demand when working on a specific area.

> Note: `modes/` and `recipes/` are project-convention docs the agent READS — they are not Claude Code's harness-registered Skills. The agent should never call the `Skill` tool for these.

`archive/` holds superseded reference material — read-only, occasionally cleanable.

## Quick map

| Looking for | Go to |
|---|---|
| What workflow runs when I type `AUTOTEST`? | `recipes/AUTOTEST.md` |
| What is RELEASE mode vs EXPERIMENTAL mode? | `modes/README.md` |
| How does the engine layout / scenario / suppression / aircraft system work? | `reference/architecture.md` |
| Balance dashboard + autotest harness workflow | `recipes/BALANCE.md` |
| Big-picture project state | `reference/project-assessment.md` |
| Shadow / firing-LOS roadmap | `reference/shadow-los-plan.md` |
| Live tracker / current focus / backlog | `WORKSPACE/` (separate top-level folder) |
| Agent instructions | `CLAUDE.md` (root) |
