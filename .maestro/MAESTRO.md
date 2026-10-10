# WW3MOD — manager orchestration instructions

You already have CLAUDE.md (auto-loaded) — the game-model hard rules, routing table, knowledge-bank flow, and the worker-facing half of `## Who runs what` live there. This file adds only what is manager-specific.

## Knowledge-bank curation — you own it

Workers capture to `WORKSPACE/DISCOVERIES.md`; promotion into `DOCS/reference/` is your responsibility (rules: `DOCS/reference/README.md`).

- **Dispatch a curation worker** at the end of a work batch, or when DISCOVERIES has ~10+ unpromoted entries. The brief: verify each unpromoted entry against the code (read it — don't trust memory or the entry itself), merge verified facts into the right reference doc, tag the source entry `[promoted]` or `[rejected: reason]`. Reject freely.
- **Seeding a new subject doc**: one focused research agent per subject, instructed to cite `file` + symbol for every claim and to read the cited code rather than summarize from prior context. Depth over breadth.
- A worker reporting a code-vs-doc contradiction → the doc fix is part of the same work item, not a someday-task.

Why the capture/promote split: free writes by every worker rot the bank until it has to be nuked; write-only-in-big-sessions loses the freshest context. Capture-at-discovery + verified promotion keeps both.

## The user's checkouts stay on `main`

The user tests from more than one machine and wants `main` always checked out there, with every implementation done in a worktree and merged back, so `main` is always the stable thing they test.

**Whenever you hand the user a command, or write a prompt for an agent on another machine:**

- **Never leave a checkout detached.** If a comparison needs a pinned commit, prefer a throwaway worktree at that SHA — it leaves `main` untouched and disposes cleanly.
- If checking out a SHA in the user's checkout is genuinely unavoidable, the prompt must end by returning to `main` (`git checkout main && git pull`) as a REQUIRED final step, and must say what state the machine is left in and how to undo it, in the same breath as the instruction that causes it.
- The user's checkout is a *test* environment, not scratch space. Anything that makes it non-obvious which build they are running — detached HEAD, a stray branch, uncommitted edits — costs them a play session.

The general shape: **an instruction that changes the user's environment must carry its own reversal.** When two rules in this file disagree, the one that touches the user's environment less wins.

## Who runs what — the manager's half

CLAUDE.md `## Who runs what` tells workers what they may run. This is what that policy asks of you.

### Launches and YAML lint are yours, and they run one at a time

A worker you dispatch never starts the game and never runs the YAML lint. You run both, serially, and feed results back. The user granted launch authority to the manager on the condition that several workers do not start simulations at once — concurrent game launches overload and can crash the machine — so **keep an eye on machine load** and do not stack launches. The lint is serialized for a related reason: `utility.sh --check-yaml` jobs from sibling worktrees queue behind one another (eight concurrent jobs held one worker for ~35 minutes), and the queue does not drain while the fleet runs, so one run at merge replaces many contended ones without losing coverage.

Everything that starts the game (each verified to launch, directly or through `run-test.sh`/`launch-game.sh`):

- `launch-game.sh` / `launch-game.cmd`
- `tools/autotest/run-test.sh`, `run-batch.sh`, `run-demo.sh` (wraps `run-test.sh`)
- `tools/autotest/run-tournament.sh` (calls `launch-game.sh` directly) and `loop-tournament.sh` (calls `run-tournament.sh`)
- `tools/autotest/run-smoke.sh`, i.e. `make smoke` / `.\make.ps1 smoke`
- the screenshot scripts: `start-screenshot-mode.sh`, `screenshot-lobby.sh`, `screenshot-hotkeys.sh`, `screenshot-infopanel.sh`, `screenshot-editor-zones.sh`

The YAML lint is `utility.sh --check-yaml` in any form, including `make test` / `.\make.ps1 test`, which ends with it.

Builds and static gates stay with the worker: `make all`, `make check`, `dotnet test`, and the static gates `lua-gate`, `nav-guard`, `worldactor-gate`, `smudge-gate`, `mount-gate` (each is a target in both `Makefile` and `make.ps1`).

**What every brief must carry**, because workers do not load this file and the recipes they follow by default (`DOCS/recipes/AUTOTEST.md`, `SCREENSHOT.md`, `DEMO.md`, `BALANCE.md`) describe running things yourself:

- The no-launch / no-lint clause, restated explicitly.
- For anything that needs a run: deliver the scenario **plus an explicit statement of what result counts as the answer**, so you can run it without re-deriving intent.
- For any YAML touched: list the files and **what lint would say if they got it wrong**. Check the single gate run against those statements — that keeps the worker's intent as a checkable claim.
- "Run `make all` (or `.\make.ps1 all`) in your worktree before you commit" — plus `make check` if the change touches C#.

**Building is a property of the worktree, not of the change.** `engine/bin` is not in git, so a fresh worktree has no build, and `launch-game.sh` refuses to start without `engine/bin/OpenRA.dll` and a `VERSION` matching `ENGINE_VERSION` — the game never starts and there is nothing to diagnose. A diff with no compiled code does not exempt the tree. **Launch only from a tree that has been built** — your own, or a worker's after its `make all`.

When you lint a single scenario: `./utility.sh --check-yaml ../tools/autotest/scenarios/<name>` from the repository root. The `../` is required because the root `utility.sh` cds into `engine/` before running; it lints YAML, not Lua. Never run `engine/utility.sh`: it is tracked mode `100644`, so `./` exits 126 with a zero-byte log. **Exit 126/127, or a fast non-zero exit with zero-byte output, is a launch failure — nothing ran.**

**Never `pkill -f OpenRA.Utility`** (or any name pattern): with several lint or game jobs on the machine it kills siblings' work. Find your own process (e.g. resolve its cwd with `lsof`) and kill that pid only.

### The merge gate

Run at merge, in order, before pushing `main`:

1. `all` — Release build.
2. `check` — Debug build with analyzers on. **Release strips every analyzer** (`engine/Directory.Build.props`, target `DisableAnalyzers`), and `dotnet test` also builds Release, so `check` is the only step that sees an analyzer (RCS-class) error. A green Release build is not evidence about the Debug gate.
3. `dotnet test engine/OpenRA.Test/OpenRA.Test.csproj --configuration Release`
4. `test` — static gates plus the YAML lint.
5. `smoke` — for any merge that changes C# under `engine/`. It is the only gate that constructs a World; read its exit code (0 pass, 2 a map started and failed to reach a pass, 3 launch failure — nothing proven).

Use `make <target>` on macOS/Linux and `.\make.ps1 <target>` on Windows.

A branch touching only `mods/` or `tools/` still runs `dotnet test`: NUnit fixtures read the shipped YAML (e.g. `VaporizeScopeTest` scans every `*.yaml` under `mods/ww3mod/rules`), so a YAML-only change can turn the suite red. Only the builds may be skipped, and only when the C# is provably identical to an already-gated ref.

Workers never push. You push `main` after the gate is green.

## Dispatching workers

- Name the specific reference docs a task needs (per CLAUDE.md's routing table) rather than "read the docs".
- **Every worker gets a worktree — doc workers included.** A shared checkout shares both **HEAD** (a worker's `git checkout -b` moves the branch under every party in that directory, the manager included) and **the index** (any party's `git add` + `git commit` sweeps another's staged files into the wrong commit). Dispatch with `git worktree add <path> -b <branch> main`; never write a brief that runs `git checkout` in a shared checkout. When you do work in a shared checkout yourself, **commit path-limited only** (`git commit <paths> -m ...`).
- Worktree paths: `/Users/fredrik/worktrees/ww3mod/<name>` on macOS, `C:/Users/fredr/worktrees/ww3mod/<name>` on Windows. Give the path with **forward slashes** in a brief — bash eats backslashes and the worktree lands somewhere wrong. A docs-only worktree needs no build.
- **A worker-reported suite red is usually its own stale base, and only you can settle it.** A worker cannot tell which commits landed on `main` after it forked; `git merge-base --is-ancestor <fixing-commit> <branch>` answers it from your seat. Resolve it and tell the worker before the next dispatch. General form: **any baseline a worker inherits is a claim about its branch point, not about `main`.**

## Batch sizing and the merge pipeline

**Batch by subsystem cohesion, not by size.** Larger per-worker batches win when the items share one subsystem — same files, same concepts, or B-consumes-A ordering: one dispatch-and-review cycle instead of several with no drop in review quality. Don't bundle across subsystems — the brief bloats and the reviewer loses a single story to check.

- **The cap is one clean brief.** Headings per item are fine; once items need *different* reference docs and constraints, split.
- **Pipeline shape, regardless of batch size:** implementer on an isolated worktree → explicit do-NOT-merge brief → independent adversarial reviewer (read-only) → manager merges on a green gate and routes FIX items back to the *same* implementer (it has the context; one fix commit, no amend). Reviews have caught real defects (a danger-channel leak, an RNG-stream identity break, an unsafe carrier rule) — the reviewer cost is paid for.
- **Review sizing:** full adversarial reviewer for behaviour/engine changes; test-only or byte-identical batches can take a manager diff-inspection on merge instead.
- **Known merge frictions:** `WORKSPACE/DISCOVERIES.md` conflicts append-vs-append when two branches both add entries — resolve keep-both. On Windows, `git worktree remove` fails with "Permission denied" while a worker session still holds the dir as cwd — archive the worker first, then remove (a failed first attempt usually already unregistered it; delete the leftover dir).

## Autoburn playbook

Orientation order for a fresh manager told "work the pipeline":

1. `WORKSPACE/PIPELINE.md` — the ordered queue; top item = next to start. It holds stubs only and is meant to be read whole; the dossier for a chosen item is `WORKSPACE/pipeline/items/<NN>-<slug>.md`, and finished work lives in `WORKSPACE/pipeline/archive/` ([map](../WORKSPACE/pipeline/README.md)). Items marked user-gated need explicit grants — never self-authorize. **Check an item's central premise with one `git log -S`/grep before dispatching.** **A merged branch is not a finished item** — a feature can merge switched off, or a branch named for an item can carry only test hygiene while the fix rode another; read what the branch contained. **Read the file, not the commit message** — a commit that looks like it addresses an item may touch none of its symptoms. **Two documents agreeing on a number is not evidence.**
2. `WORKSPACE/cases/README.md` — the scenario-case model: user-authored cases with ONE measurable bar each are the preferred unit of autonomous work. Iterate features/tuning until the case reads GREEN. Case files carry their own dependencies and status logs.
3. `WORKSPACE/HOTBOARD.md` + `git log --oneline -20` — what just happened.
4. The routing table in CLAUDE.md for anything a specific item touches.

Lessons that bind future windows:

- **Measurement is the product.** Shipping well-reviewed changes with no outcome numbers is the failure mode to avoid. Prefer queue items whose acceptance is a number; when a bot change ships without a valid benchmark baseline, flag it loudly in the track rather than letting it slide.
- **Grants are the bottleneck to plan around.** Case calibration and benchmarks are multi-run and user-gated. Front-load all non-gated work (recon, features, overlays, scenario authoring) and park measurement steps with a clear "needs grant" flag, so one user grant unlocks a batch of ready-to-run measurements.
- **Cap needs_review pileup.** Under the case model a GREEN bar largely self-certifies — reserve needs_review for genuine taste/feel checks and say precisely what the user should look at.
- **Persist state relentlessly.** Anything a future manager needs lives in PIPELINE / cases / DISCOVERIES / the manager log — never only in a transcript. Assume every session can be replaced mid-arc.
