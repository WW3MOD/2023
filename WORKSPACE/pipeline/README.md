# `pipeline/` — the roadmap's detail, split out of the queue

[`../PIPELINE.md`](../PIPELINE.md) is the live queue and holds **stubs only**. Everything else lives here.

## Why the split exists

`PIPELINE.md` reached 950 lines / 295 KB. It was over the file-read size limit, a worker that read it whole hit two consecutive API 529s while five siblings were fine, and managers had taken to extracting single items with `awk '/^### N\./,/^### N+1\./'`. That awk workaround is the design this directory makes official: **the unit of dispatch is one item, so one item is one file.**

The queue also had an accuracy problem the size made worse — in the week to 2026-08-19, five items were found to describe already-merged work, two of them after a worker had been dispatched. A short file makes that cheaper to catch.

## Layout

```
PIPELINE.md                    live queue — stubs, ranked, read this whole
pipeline/
  README.md                    this file
  items/<NN>-<slug>.md         full dossier per live item, verbatim
  archive/closed-items.md      closed/retired/shipped numbered items + R-findings
  archive/shipped-log.md       the chronological SHIPPED ledger
  archive/session-notes.md     dated snapshots, batch framing, method notes
```

Small items (17, 18, 22, 32) stay inline in `PIPELINE.md` — a dossier file for four lines is not worth the indirection.

## Working rules

**Adding an item.** Give it the next unused number. **Numbers are never reused**, because the whole repo references items by number — `PIPELINE item 40` appears in engine C#, mod YAML, autotest scenarios and NUnit test headers. Write a stub in `PIPELINE.md` (title, status tag, a "Perceived" line, a link) and put the detail in `items/`. If it is under ~15 lines it can stay inline.

**Closing an item.** Move the whole dossier into `archive/closed-items.md` — **do not delete it, and do not summarise it.** Delete the stub from `PIPELINE.md`. If the item leaves behind a ruling, a trap or a "do not re-propose this", add a row to the table at the top of `closed-items.md` so it stays findable. Add a line to `archive/shipped-log.md` if it shipped code.

**Before dispatching anyone at an item**, spend one `git log -S <symbol>` or one grep on its central premise. This is the cheapest check in the project and it is the one this queue has most often skipped. Stubs carrying ⚠️ in `PIPELINE.md` already failed it once.

**But grepping for the defect only proves the defect is real — it cannot tell you the item is DONE.** Three ways that check passes while the answer is wrong (all observed 2026-09-02):

- **A merged branch is not a finished item.** One item's branch was already an ancestor of `main` while the feature still shipped switched off. `git merge-base --is-ancestor` answers *did this land*, not *is this on*. **Read the value the running code reads** — the `RequiresCondition`, the default on the Info field, the YAML that grants it.
- **A named branch is not the change.** Another item's branch carried only test hygiene while the real fix rode a different one. Search for the *behaviour* across branches, not for the item's name.
- **Read the file, not the commit message.** One finding was nearly closed on a commit whose subject matched exactly and whose diff turned out to be a `PIPELINE.md` edit. And **two documents agreeing on a number is not evidence** — docs get copied from each other; only the code is a source.

**Keep the live file readable whole.** That is the acceptance bar, not a style preference. If `PIPELINE.md` starts growing dossier-shaped prose again, move it here.

## What survives the split, and what does not

**Item-number references are stable and unaffected.** Anything citing `PIPELINE item 40` still resolves — check the queue, then `items/`, then `archive/closed-items.md`.

**Line-number references are broken.** Anything written before 2026-08-19 citing `PIPELINE.md:NNN` points into the pre-split file; resolve those against `git show de78a1ed:WORKSPACE/PIPELINE.md`. Known holders, all dated snapshots deliberately left unedited: `WORKSPACE/audit/260816-build-test-health.md`, `WORKSPACE/cargo-garrison-status-260819.md`, `WORKSPACE/garrison-proposals.md`, `WORKSPACE/lobby/UX_REVIEW_260819.md`, `WORKSPACE/scoping/neutralise-capture.md`, `WORKSPACE/research/frontline-influence.md`, `WORKSPACE/lobby-ux-mockup.html`, `WORKSPACE/DISCOVERIES.md:1242`.

## Relationship to the other boards

| File | Holds |
|---|---|
| `PIPELINE.md` | the ordered plan of attack |
| `RELEASE_V1.md` | source of truth for v1 **scope** |
| `HOTBOARD.md` | what is **in motion** right now |
| `AWAITING-USER.md` | everything parked on a user decision, review or grant |
| `DISCOVERIES.md` | durable insights, dated, pending promotion into `DOCS/reference/` |
| `bugs/discovered.md` | incidental defects found in passing |
