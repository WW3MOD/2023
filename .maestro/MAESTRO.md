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
- **A fresh worktree cannot launch the game — say so in every brief that will run or screenshot anything.** `git worktree add` does not bring build output across and `engine/bin` is not in git, so a new worktree has none. `run-test.sh` and `launch-game.sh` both launch from the *worktree's own* `engine/bin`, and `launch-game.sh:42` gates on `OpenRA.dll` present + `VERSION` matching `ENGINE_VERSION`. Missing ⇒ the game never starts: `NO-RESULT (exit 3)`, `lua.log` 0 bytes, run dir empty, nothing to diagnose. **This burned a scarce run grant on 2026-08-17**, and the reasoning that caused it is the reusable part: the worker skipped the build because its *diff* contained no compiled code — true about the diff, irrelevant to whether the game can start from that directory. **Building is a property of the worktree, not of the change.** Boilerplate for any brief involving a run or a screenshot: *"run `make all` inside your worktree before your first launch, not just before your commit."* Free partial substitute worth naming too: `./utility.sh --check-yaml <MAPDIR>` lints a SINGLE map, launches nothing, needs only a build — it validates YAML but **not** Lua.
- **A worker-reported suite red is usually its own stale base, and only you can settle it.** Two workers in one hour reported ~95 `make test` cordon errors, both correctly declined to chase them, and neither could prove the red wasn't theirs — because knowing *which* commit fixed it requires knowing what landed on `main` after their fork. `git merge-base --is-ancestor <fixing-commit> <branch>` answers it instantly from your seat. Resolve it and tell the worker before the next dispatch; leaving it costs report space and leaves you holding a phantom caveat. General form: **any baseline a worker inherits is a claim about its branch point, not about `main`** — the same shape produced the stale-`[IN FLIGHT]` misdispatch of 2026-08-16.
- **Shared-index hazard (doc-only workers in the main checkout)**: everyone in the same checkout shares ONE git index. A `git add <file> && git commit` by the manager (or any worker) sweeps another party's staged-but-uncommitted files into the wrong commit (happened at 7385c055). Rules: while workers are active in the main repo, commit path-limited only (`git commit <paths> -m ...`, never bare `git add`+`commit`); tell each concurrent doc worker to commit its specific files by path; anything bigger than a doc tweak goes to a worktree as usual.

## Autoburn playbook (added 2026-07-26, after the first-window retrospective)

Orientation order for a fresh manager told "work the pipeline":

1. `WORKSPACE/PIPELINE.md` — the ordered queue; top item = next to start. Items marked user-gated need explicit grants — never self-authorize. **It holds stubs only and is meant to be read whole**; the dossier for a chosen item is `WORKSPACE/pipeline/items/<NN>-<slug>.md`, and finished work lives in `WORKSPACE/pipeline/archive/` ([map](../WORKSPACE/pipeline/README.md)). **Check an item's central premise with one `git log -S`/grep before dispatching** — stale items have twice cost a worker.
2. `WORKSPACE/cases/README.md` — the scenario-case model: user-authored cases with ONE measurable bar each are the preferred unit of autonomous work. Iterate features/tuning until the case reads GREEN. Case files carry their own dependencies and status logs.
3. `WORKSPACE/HOTBOARD.md` + `git log --oneline -20` — what just happened.
4. The routing table in CLAUDE.md for anything a specific item touches.

Retrospective lessons that bind future windows:

- **Measurement is the product.** The first window's failure mode was shipping well-reviewed changes with no outcome numbers (the Stage-F benchmark re-baseline sat declared-never-run). Prefer queue items whose acceptance is a number; when a bot change ships without a valid benchmark baseline, flag it loudly in the track rather than letting it slide.
- **Grants are the bottleneck to plan around.** Case calibration and benchmarks are user-gated (no-autonomous-multi-test). Front-load all NON-gated work (recon, features, overlays, scenario authoring) and park measurement steps with a clear "needs grant" flag, so a single user grant unlocks a batch of ready-to-run measurements instead of one.
- **Cap needs_review pileup.** Ten subjective-review tracks accumulated in window 1. Under the case model, a GREEN bar largely self-certifies — reserve needs_review for genuine taste/feel checks and say precisely what the user should look at.
- **Persist state relentlessly.** Anything a future manager needs lives in PIPELINE / cases / DISCOVERIES / the manager log — never only in a transcript. Assume every session can be replaced mid-arc.
