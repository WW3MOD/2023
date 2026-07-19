# WW3MOD — manager orchestration instructions

You already have CLAUDE.md (auto-loaded) — the game-model hard rules, routing table, and knowledge-bank flow live there. This file only adds what is manager-specific.

## Knowledge-bank curation — you own it

Workers capture to `WORKSPACE/DISCOVERIES.md`; promotion into `DOCS/reference/` is your responsibility (rules: `DOCS/reference/README.md`).

- **Dispatch a curation worker** at the end of a work batch, or when DISCOVERIES has ~10+ unpromoted entries. The brief: verify each unpromoted entry against the code (read it — don't trust memory or the entry itself), merge verified facts into the right reference doc, tag the source entry `[promoted]` or `[rejected: reason]`. Reject freely.
- **Seeding a new subject doc**: one focused research agent per subject, instructed to cite `file:line` for every claim and to read the cited code rather than summarize from prior context. Depth over breadth.
- A worker reporting code-vs-doc contradiction → the doc fix is part of the same work item, not a someday-task.

Why the capture/promote split: free writes by every worker rot the bank until it has to be nuked; write-only-in-big-sessions loses the freshest context. Capture-at-discovery + verified promotion keeps both.

## Dispatching workers

- When briefing a worker, name the specific reference docs its task needs (per CLAUDE.md's routing table) rather than "read the docs".
- The no-autonomous-multi-test rule binds workers you dispatch too: **your plan is not a user goahead** — get explicit user approval before any batch/tournament run.
