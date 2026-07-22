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

## Batch sizing + the merge pipeline (findings, 2026-07-22 autoburn)

Deliberate experiment: larger per-worker batches vs one-item-per-worker, across five waves (Stage 0 solo; Stages A+B combined; Phase 4a; Stage C large; a 4-item test-hardening batch). Verdict: **larger batches win when the items share one subsystem** — same files, same concepts, or B-consumes-A ordering. A+B in one worker cost one dispatch+review cycle instead of two with no drop in review quality; the 4-item hardening batch landed as one coherent commit. Guidance for future managers:

- **Batch by subsystem cohesion, not by size.** Bundle items that read the same code and would each need the same warm-up. Don't bundle across subsystems — the brief bloats and the reviewer loses a single story to check.
- **The cap is one clean brief.** If the brief needs headings per item to stay readable (worked at 4 items), fine; if items start needing *different* reference docs and constraints, split.
- **Keep the pipeline shape regardless of batch size**: implementer on an isolated worktree under `C:\Users\fredr\worktrees\ww3mod\<name>` → explicit do-NOT-merge brief → independent adversarial reviewer (read-only) → manager merges on green and routes FIX items back to the *same* implementer (it has the context; one fix commit, no amend). Reviews caught 3 real defects across the window (ICBM danger-channel leak, RNG-stream identity break, an unsafe carrier rule) — the reviewer cost is paid for.
- **Review sizing**: full adversarial reviewer for behavior/engine changes; test-only or byte-identical batches can take a manager diff-inspection on merge instead.
- **Known merge frictions**: `WORKSPACE/DISCOVERIES.md` conflicts append-vs-append when two branches both add entries — resolve keep-both. Windows: `git worktree remove` fails with "Permission denied" while a worker session still holds the dir as cwd — archive the worker first, then remove (a failed first attempt usually already unregistered it; just `rm -rf` the leftover dir). Worker-created worktrees: give the path with FORWARD slashes in the brief, or bash eats the backslashes and the worktree lands somewhere wrong.
