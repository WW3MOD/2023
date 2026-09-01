# WW3MOD — manager orchestration instructions

You already have CLAUDE.md (auto-loaded) — the game-model hard rules, routing table, and knowledge-bank flow live there. This file only adds what is manager-specific.

## Knowledge-bank curation — you own it

Workers capture to `WORKSPACE/DISCOVERIES.md`; promotion into `DOCS/reference/` is your responsibility (rules: `DOCS/reference/README.md`).

- **Dispatch a curation worker** at the end of a work batch, or when DISCOVERIES has ~10+ unpromoted entries. The brief: verify each unpromoted entry against the code (read it — don't trust memory or the entry itself), merge verified facts into the right reference doc, tag the source entry `[promoted]` or `[rejected: reason]`. Reject freely.
- **Seeding a new subject doc**: one focused research agent per subject, instructed to cite `file:line` for every claim and to read the cited code rather than summarize from prior context. Depth over breadth.
- A worker reporting code-vs-doc contradiction → the doc fix is part of the same work item, not a someday-task.

Why the capture/promote split: free writes by every worker rot the bank until it has to be nuked; write-only-in-big-sessions loses the freshest context. Capture-at-discovery + verified promotion keeps both.

## The user's checkouts must be left on `main` — standing rule (2026-08-30)

The user tests from a second machine and stated the rule directly: **"I want main always checked out (unless there is a good reason not to) and every implementation is done in worktrees and merged back into main, so main is always stable for me to test."**

That is already how this machine works. The failure was on the *other* machine, and it was a manager's doing: a prompt sent for a cross-machine comparison said `git checkout <40-hex sha>` so both halves would hash identical code — correct, and it left the user staring at `((HEAD detached at 55836dd8))`, silently pinned four merges behind `main`.

**Whenever you hand the user a command, or write a prompt for an agent on another machine:**

- **Never leave a checkout detached.** If you must pin a commit for a comparison, the prompt must end by returning to `main` (`git checkout main && git pull`) as a REQUIRED final step, not a courtesy.
- Prefer a throwaway worktree at the pinned SHA over checking out a SHA in the user's working checkout at all — a worktree leaves `main` untouched and disposes cleanly.
- If a pinned checkout is genuinely unavoidable, **say in the prompt what state the machine will be left in and how to undo it**, in the same breath as the instruction that causes it.
- The user's checkout is a *test* environment, not scratch space. Anything that makes it non-obvious which build they are running — detached HEAD, a stray branch, uncommitted edits — costs them a play session and is worse than the problem it solved.

The general shape: **an instruction that changes the user's environment must carry its own reversal.** Same discipline as removing a worktree after a merge, and it failed for the same reason — the task felt finished the moment the answer arrived.

## Dispatching workers

- When briefing a worker, name the specific reference docs its task needs (per CLAUDE.md's routing table) rather than "read the docs".
- ~~The no-autonomous-multi-test rule binds workers you dispatch too: **your plan is not a user goahead** — get explicit user approval before any batch/tournament run.~~ **SUPERSEDED 2026-08-19 — see the two operating rules below.** The gate is no longer "ask the user before a batch", it is **"only the manager runs it"**.

## Two standing operating rules — added 2026-08-19, re-confirmed in practice 2026-09-01

These are manager-facing on purpose. Workers never see this file, so **both must be restated in every worker brief** or the worker will follow `DOCS/recipes/AUTOTEST.md` and `SCREENSHOT.md`, which still tell it to run things itself.

### 1. Launches serialize through the manager. Workers never start the game.

The user granted full simulation/launch authority to the *manager* and attached a hard constraint, verbatim:

> "You have full grants to launch simulations but I suggest you do it from here so that multiple workers are not all starting simulations. That will crash my computer. So keep an eye on the load and make sure you dont completely overload the machine."

**The manager is the only party that launches anything.** No worker runs `launch-game.sh`, `run-test.sh`, `run-batch.sh`, `run-tournament.sh`, or any screenshot capture. A worker writes down what it needs run and hands it up; the manager runs it serially and feeds the result back.

Consequences to carry:
- **Every brief needs the no-launch clause explicitly**, because it contradicts the recipes the worker is told to follow by default.
- The implement→verify loop is now split across two parties. Ask each worker for the scenario file **plus an explicit "what would count as the answer"**, so the manager can run it without re-deriving intent.
- The worktree-build rule inverts in part: a fresh worktree still needs `make all` to compile-check, but no longer "before the first launch" — the worker never launches. **The manager's own launch must come from a tree that IS built.**

### 2. Workers do not run the YAML validator. The manager runs it once, at merge.

At ten concurrent workers `./utility.sh --check-yaml` became a hard serialization point. Measured 2026-08-19: one worker's validator **never got a turn** (0-byte output, idle long enough to trip a stall warning), and another measured **eight concurrent lint jobs** across sibling worktrees with its own waiting **~35 minutes**. The queue does not drain while the fleet is running, so "wait it out" is not a strategy.

**Rule: workers run neither `./utility.sh --check-yaml` nor `make test`. The manager runs the YAML gate serially at merge time.** The merge gate is the one that actually protects `main`; a worker's local run is redundant with it and at this fleet size its only marginal effect is queue depth. `make all` and `dotnet test` stay with the worker — neither is contended.

**Compensating requirement, so nothing is lost:** each worker must list in its report **which YAML files it touched and what it would expect lint to say if it got it wrong.** The manager checks the single gate run against those statements — that keeps the worker's intent as a checkable claim instead of discarding it.

**Related hazard, restated because it nearly fired: never `pkill -f OpenRA.Utility`.** With eight concurrent jobs that kills seven siblings' work. Resolve the cwd with `lsof` and kill only your own pid.

## Batch sizing + the merge pipeline (findings, 2026-07-22 autoburn)

Deliberate experiment: larger per-worker batches vs one-item-per-worker, across five waves (Stage 0 solo; Stages A+B combined; Phase 4a; Stage C large; a 4-item test-hardening batch). Verdict: **larger batches win when the items share one subsystem** — same files, same concepts, or B-consumes-A ordering. A+B in one worker cost one dispatch+review cycle instead of two with no drop in review quality; the 4-item hardening batch landed as one coherent commit. Guidance for future managers:

- **Batch by subsystem cohesion, not by size.** Bundle items that read the same code and would each need the same warm-up. Don't bundle across subsystems — the brief bloats and the reviewer loses a single story to check.
- **The cap is one clean brief.** If the brief needs headings per item to stay readable (worked at 4 items), fine; if items start needing *different* reference docs and constraints, split.
- **Keep the pipeline shape regardless of batch size**: implementer on an isolated worktree under `C:\Users\fredr\worktrees\ww3mod\<name>` → explicit do-NOT-merge brief → independent adversarial reviewer (read-only) → manager merges on green and routes FIX items back to the *same* implementer (it has the context; one fix commit, no amend). Reviews caught 3 real defects across the window (ICBM danger-channel leak, RNG-stream identity break, an unsafe carrier rule) — the reviewer cost is paid for.
- **Review sizing**: full adversarial reviewer for behavior/engine changes; test-only or byte-identical batches can take a manager diff-inspection on merge instead.
- **Known merge frictions**: `WORKSPACE/DISCOVERIES.md` conflicts append-vs-append when two branches both add entries — resolve keep-both. Windows: `git worktree remove` fails with "Permission denied" while a worker session still holds the dir as cwd — archive the worker first, then remove (a failed first attempt usually already unregistered it; just `rm -rf` the leftover dir). Worker-created worktrees: give the path with FORWARD slashes in the brief, or bash eats the backslashes and the worktree lands somewhere wrong.
- **A fresh worktree cannot launch the game — say so in every brief that will run or screenshot anything.** `git worktree add` does not bring build output across and `engine/bin` is not in git, so a new worktree has none. `run-test.sh` and `launch-game.sh` both launch from the *worktree's own* `engine/bin`, and `launch-game.sh:42` gates on `OpenRA.dll` present + `VERSION` matching `ENGINE_VERSION`. Missing ⇒ the game never starts: `NO-RESULT (exit 3)`, `lua.log` 0 bytes, run dir empty, nothing to diagnose. **This burned a scarce run grant on 2026-08-17**, and the reasoning that caused it is the reusable part: the worker skipped the build because its *diff* contained no compiled code — true about the diff, irrelevant to whether the game can start from that directory. **Building is a property of the worktree, not of the change.** Boilerplate for any brief involving a run or a screenshot: *"run `make all` inside your worktree before your first launch, not just before your commit."* Free partial substitute worth naming too: `./utility.sh --check-yaml <MAPDIR>` lints a SINGLE map, launches nothing, needs only a build — it validates YAML but **not** Lua.
- **A worker-reported suite red is usually its own stale base, and only you can settle it.** Two workers in one hour reported ~95 `make test` cordon errors, both correctly declined to chase them, and neither could prove the red wasn't theirs — because knowing *which* commit fixed it requires knowing what landed on `main` after their fork. `git merge-base --is-ancestor <fixing-commit> <branch>` answers it instantly from your seat. Resolve it and tell the worker before the next dispatch; leaving it costs report space and leaves you holding a phantom caveat. General form: **any baseline a worker inherits is a claim about its branch point, not about `main`** — the same shape produced the stale-`[IN FLIGHT]` misdispatch of 2026-08-16.
- **Shared-index hazard (doc-only workers in the main checkout)**: everyone in the same checkout shares ONE git index. A `git add <file> && git commit` by the manager (or any worker) sweeps another party's staged-but-uncommitted files into the wrong commit (happened at 7385c055). Rules: while workers are active in the main repo, commit path-limited only (`git commit <paths> -m ...`, never bare `git add`+`commit`); tell each concurrent doc worker to commit its specific files by path; anything bigger than a doc tweak goes to a worktree as usual.

## Autoburn playbook (added 2026-07-26, after the first-window retrospective)

Orientation order for a fresh manager told "work the pipeline":

1. `WORKSPACE/PIPELINE.md` — the ordered queue; top item = next to start. Items marked user-gated need explicit grants — never self-authorize. **It holds stubs only and is meant to be read whole**; the dossier for a chosen item is `WORKSPACE/pipeline/items/<NN>-<slug>.md`, and finished work lives in `WORKSPACE/pipeline/archive/` ([map](../WORKSPACE/pipeline/README.md)). **Check an item's central premise with one `git log -S`/grep before dispatching** — stale items have twice cost a worker. **Sharper form, earned 2026-08-19 and re-confirmed 2026-09-01: a merged branch is not a finished item.** Item 64's branch is an ancestor of `main` and the feature still ships switched OFF; another item's named branch carried only test hygiene while the real fix rode a different one. Read what the branch *contained*. **And read the file, not the commit message** — one finding was nearly closed on the strength of `ed5ee6b6`, which turned out to be a `PIPELINE.md` edit, and R7 has since attracted two more commits that looked like they addressed it and touched none of its five symptoms. **Two documents agreeing on a number is not evidence.**
2. `WORKSPACE/cases/README.md` — the scenario-case model: user-authored cases with ONE measurable bar each are the preferred unit of autonomous work. Iterate features/tuning until the case reads GREEN. Case files carry their own dependencies and status logs.
3. `WORKSPACE/HOTBOARD.md` + `git log --oneline -20` — what just happened.
4. The routing table in CLAUDE.md for anything a specific item touches.

Retrospective lessons that bind future windows:

- **Measurement is the product.** The first window's failure mode was shipping well-reviewed changes with no outcome numbers (the Stage-F benchmark re-baseline sat declared-never-run). Prefer queue items whose acceptance is a number; when a bot change ships without a valid benchmark baseline, flag it loudly in the track rather than letting it slide.
- **Grants are the bottleneck to plan around.** Case calibration and benchmarks are user-gated (no-autonomous-multi-test). Front-load all NON-gated work (recon, features, overlays, scenario authoring) and park measurement steps with a clear "needs grant" flag, so a single user grant unlocks a batch of ready-to-run measurements instead of one.
- **Cap needs_review pileup.** Ten subjective-review tracks accumulated in window 1. Under the case model, a GREEN bar largely self-certifies — reserve needs_review for genuine taste/feel checks and say precisely what the user should look at.
- **Persist state relentlessly.** Anything a future manager needs lives in PIPELINE / cases / DISCOVERIES / the manager log — never only in a transcript. Assume every session can be replaced mid-arc.
