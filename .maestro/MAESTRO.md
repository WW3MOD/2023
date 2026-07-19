# WW3MOD — manager orchestration instructions

## Knowledge-bank curation (manager-owned responsibility)

`DOCS/reference/` is this repo's knowledge bank. Its value is that claims there are trusted without re-verification — **you own protecting that property**. Workers capture; you curate. Full rules: `DOCS/reference/README.md`.

**The flow you enforce:**

- Workers append new insights to `WORKSPACE/DISCOVERIES.md` (dated, code-cited). They do NOT write new knowledge into `DOCS/reference/` directly — only on-sight corrections of verifiably wrong statements.
- **Periodically dispatch a curation worker** — good triggers: end of a work batch, or when DISCOVERIES has ~10+ unpromoted entries. The curation brief: verify each unpromoted entry against the code (read it, don't trust memory or the entry itself), merge verified facts into the right reference doc, tag the source entry `[promoted]` or `[rejected: reason]`. Reject freely — an unverifiable claim never lands in reference.
- **Seeding a new subject doc**: dispatch one focused research agent per subject with explicit instructions to cite `file:line` for every claim and to read the cited code, not summarize from prior context. One subject per session — depth over breadth.
- If a worker reports that code contradicts a reference doc, treat the doc fix as part of the same work item, not a someday-task. Never leave doc and code contradicting each other.

**Why this split:** continuous autonomous writes by every worker rot the bank (unverified claims accumulate until the whole thing has to be nuked); write-only-in-big-sessions loses the freshest context. Capture-at-discovery + verified-promotion keeps both.

## Worker briefing notes

- `CLAUDE.md` (worker-facing) is intentionally minimal: hard rules + a routing table. When briefing a worker, name the specific reference docs their task needs (per the routing table) rather than telling them to "read the docs".
- The AUTOTEST batch restriction in CLAUDE.md's hard rules applies to workers you dispatch too — a manager's plan is not a user goahead. Get explicit user approval before any multi-test run.
