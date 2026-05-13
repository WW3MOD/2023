# Automation Workflow Track

> **Purpose.** Turn the tools we've built (AUTOTEST, DEMO, SCREENSHOT, tournament harness) into a workflow that compounds.
>
> **Why this exists.** The tools are solid in isolation. What's missing is the connective tissue: lanes for when to use which, a non-interrupting Windows path, a queue model for batched and overnight work, an orchestrator that survives single-session timeouts, and a visual feedback channel that lets the agent communicate gameplay state without the user firing the game up.
>
> **Status.** Plan landed 2026-05-13. Phase 0 not started. Phases below are sequenced for shippable improvement at every step — even if we stop after Phase 1, the workflow is meaningfully better than today.

---

## North Star

> The user can hand the agent a backlog of well-scoped tasks at 11pm, walk away, and wake up to find work shipped on a `night-260514` branch with a one-page briefing of what changed, what's verified, and what needs eyes. During the day, focused sessions stay fast — tests run silent in the background, only the verdict shows up in chat, and nothing ever pulls focus while typing.

---

## Phase 0 — Stop stealing focus (Windows) — 1 evening

**Problem.** `run-test.sh` advertises `--background` (default) but the implementation uses `osascript -e 'tell application System Events ...'` which is macOS-only. On Windows, the SDL window is created and immediately given foreground/activation — there is no equivalent backgrounding pass — so every autotest launch yanks the active window away from the user mid-keystroke. The `--minimized` workaround used `SDL_MinimizeWindow` but reportedly broke "switch to it later" semantics in some Windows builds (recurring bug per the brief). The user's hard preference is **never steal focus, even at the cost of being able to see/switch into the window**.

**Approach.**
1. Detect platform in `run-test.sh`. On Windows, the `--background` path takes a different branch.
2. Use one of three Windows-side strategies, in order of preference:
   - **(a) Off-screen window position.** Pass an SDL window position arg that places the window at e.g. `(-32000, -32000)` so the OS never gives it foreground (Windows ignores activation on off-screen creation). Reachable by `Alt+Tab` for a peek, but never grabbed by the cursor. Lowest risk.
   - **(b) `SW_SHOWNOACTIVATE` via a tiny PowerShell shim.** A pre-launch PS one-liner sets a hook so the game window appears without activation. Doable but fiddly across SDL backends.
   - **(c) Headless-lite launch arg.** Reuse the tournament harness's `Graphics.MaxFramerate=1` plus a new `Test.WindowMode=HeadlessLite` that creates a 1×1 hidden window. Best for batch runs; degraded for "I want to peek mid-test".
3. **Cheap fallback.** If none of these are practical: pass `--minimized` as the new Windows default and fix the "can't restore" bug head-on (likely an SDL `SDL_RestoreWindow` call missing from the autotest exit path). Document the trade-off.

**Ship criterion.** User can launch any autotest while typing in another window and **the typed text continues uninterrupted in the original window**. Test by typing a sentence while the test runs.

**Files touched.**
- `tools/autotest/run-test.sh` — platform detection, Windows branch
- `engine/OpenRA.Game/Sdl2PlatformWindow.cs` or similar — if off-screen path needs engine support
- `tools/autotest/start-screenshot-mode.sh` — same treatment for screenshot-mode launches (user-driven, but lobby still steals on startup)
- New `tools/autotest/test-focus-steal.ps1` — manual repro/verification script

**Risk.** SDL on Windows may force foreground on first window creation regardless of position. Off-screen with `SDL_HINT_VIDEO_MINIMIZE_ON_FOCUS_LOSS=0` may help.

**Won't do here.** Splitting `--background` into a richer matrix (`--background-but-listable`, `--headless`, etc.) — keep one flag, one behavior, swap implementation by platform.

---

## Phase 1 — Three explicit test lanes — 1-2 evenings

**Problem.** The agent decides ad-hoc whether to run tests, with no consistent rule. Sometimes it runs nothing for behavioral changes (regression sneaks in); sometimes it runs a 60-second test for a one-character fix. The user has asked for predictable lanes: "trivial = nothing, quick = minimal, batch = save for later."

**The lanes.**

| Lane | When | What runs | Where output goes |
|---|---|---|---|
| **TRIVIAL** | One-line fixes, typos, doc edits, YAML pretty-print | Nothing | End-of-message block, no `🧪` line |
| **QUICK** | Single-file behavioral change, isolated trait, single weapon | 1–3 named autotests, serial, headless-lite | End-of-message block, `🧪` line shows pass/fail count |
| **BATCH** | Cross-cutting change, new system, anything touching shared engine code | Full relevant sweep — queued, not auto-run | `WORKSPACE/tests_pending.md`, agent says "5 tests queued — `BATCH` to run" |

**Decision rule (added to CLAUDE.md).**
- Default lane is QUICK for behavioral fixes.
- The agent declares lane at the start of any code-changing turn: "Lane: QUICK — will run `test-foo` and `test-bar` after the fix."
- Lane can be overridden by user: "no test", "batch this", "quick only".

**Implementation.**
- New `WORKSPACE/tests_pending.md` — append-only queue with timestamp, reason, source commit hash.
- New `tools/autotest/run-queue.sh` — drains the queue, runs serially headless-lite, writes a consolidated `~/.ww3mod-tests/batch_<run-id>/summary.json`.
- Update CLAUDE.md "Workflow Rules" with the lane decision rule.
- Update AUTOTEST recipe with the lane vocabulary.

**Ship criterion.** Across 10 consecutive sessions, agent declares lane every code turn. Zero "I ran nothing" surprises for behavioral changes. Zero "it took 90 seconds to fix a typo" frustrations.

**Won't do here.** Per-subsystem test maps ("touching artillery → run these 4 tests automatically") — that's Phase 2's job. Phase 1 just gets the lanes named.

---

## Phase 2 — Subsystem → test map, plus batch consolidation — 2-3 evenings

**Problem.** Even with lanes, the agent has to *know* which tests are relevant. Today this is guessed each time. A subsystem-to-test map removes the guesswork.

**Approach.**
1. **Tag every existing autotest** with the subsystems it covers. New file: `tools/autotest/scenarios/<name>/tags.txt` — one tag per line. Examples: `artillery`, `wgm`, `heli`, `supply`, `crew-evac`, `pathfinding`.
2. **`run-batch.sh` gains `--tag <tag>`** — runs every scenario carrying that tag.
3. **Agent reads `tags.txt` files at session start**, builds a mental subsystem → tests map.
4. **Agent declares the trigger** when entering QUICK or BATCH lane: "Touched `Missile.cs` → tags `wgm,missile` → 4 tests queued."
5. **Batch run consolidation.** `run-queue.sh` (from Phase 1) extends to:
   - Run serially with headless-lite + background (Phase 0).
   - On any FAIL: stop the batch, write a `BATCH_FAILED.md` to repo root with the failing test name, verdict JSON path, and re-run command. Terminal bell rings.
   - On all PASS: append a one-line note to the most recent commit message in the form `[batch: 7/7 ✓]`.

**Ship criterion.** Agent's "I'll run X tests" decision can be reconstructed from the diff alone, no guesswork. Batch runs of 5–10 tests complete in 5–8 minutes wall-clock and produce one verdict file the user can grep.

**Files touched.**
- New `tools/autotest/scenarios/*/tags.txt` files (one pass to backfill all existing scenarios)
- `tools/autotest/run-batch.sh` — `--tag` flag, `--auto-only` flag (skip red-tracked / manual tests — addressed in `autotester_improvements.md`)
- `tools/autotest/run-queue.sh` — fail-stop, bell, append-to-commit
- `CLAUDE.md` — document subsystem map convention
- `DOCS/recipes/AUTOTEST.md` — tag declaration step

**Won't do here.** Auto-discovery of tags from `description.txt` keywords — too brittle. Manual tagging once, durable.

---

## Phase 3 — Visual feedback channel (screenshot diffs in chat) — 2-3 evenings

**Problem.** Today, when the agent changes a sprite, palette, or visual effect, the user has to launch the game manually to verify. The screenshot infra exists; what's missing is the *workflow* that uses it for proactive show-and-tell.

**Approach.**
1. **`VISUAL <topic>` recipe** in `DOCS/recipes/VISUAL.md`. Trigger fires on natural language too ("show me the new sprite", "compare before/after").
2. **Pre/post composite tool.** New `tools/autotest/composite.py` — reads two PNGs, slaps them side-by-side with labels ("Before", "After"), writes a third PNG. Uses Pillow (already installed somewhere; pin to `requirements.txt`).
3. **Agent workflow for visual changes:**
   - Identifies change is visual (new sprite, palette tweak, animation, effect, UI layout).
   - Runs a DEMO scenario *before* the change (stash WIP, build, screenshot, label `pre`).
   - Applies the change, rebuilds, runs same DEMO, screenshots, labels `post`.
   - Composites side-by-side.
   - Pastes path into end-of-message block as a `📁` line, plus a one-line semantic read.
4. **Convention.** A DEMO scenario named `demo-visual-<subsystem>` exists for each subsystem we visually iterate on (sprite-soldier, sprite-vehicle, ui-lobby, vfx-explosion, etc.). One-shot scenarios that pose the actor, zoom, wait one frame, screenshot, exit.

**Ship criterion.** User changes a sprite at 9pm, gets a side-by-side composite in chat at 9:03pm without launching the game manually.

**Files touched.**
- New `DOCS/recipes/VISUAL.md`
- New `tools/autotest/composite.py` + entry in `requirements.txt`
- New `tools/autotest/scenarios/demo-visual-*` scenarios — one per subsystem as needed
- `CLAUDE.md` — VISUAL added to the modes/recipes table

**Open question.** Composite resolution and aspect — square for chat, or full game-window resolution? Default to 1600px wide composite, downscale source PNGs to fit.

**Won't do here.** Animated GIFs of before/after sequences — overkill for v1 of this lane. Side-by-side stills cover 90% of needs.

---

## Phase 4 — Autonomous task queue (the discovered-work file) — 1 evening

**Problem.** Bugs and ideas surface continuously during sessions. Today they go to `WORKSPACE/bugs/discovered.md` or `BACKLOG.md` with no structure for "is this fit for autonomous work?". The user wants a file that, when a night-run begins, says "here are 12 things in priority order, with everything an unattended agent needs to start."

**Approach.**
1. **New file `WORKSPACE/automation/autonomous_queue.md`.** Each entry:
   ```
   ## <task-slug> — <one-line summary>
   - **Tier:** 🟢 / 🟡 / 🔴
   - **Scope:** files / subsystems
   - **Exit criteria:** how the agent knows it's done (test name, condition, etc.)
   - **Plan doc:** `WORKSPACE/plans/<file>` (must exist before tier 🟢)
   - **Depends on:** <other task-slug or "none">
   - **Estimated work:** S/M/L
   - **Notes:** anything the agent should know
   ```
2. **Tier rules.**
   - 🟢 **Autonomous-ready.** Scope clear, exit criteria written, plan doc exists, no design decisions remaining. Agent can land it solo.
   - 🟡 **Autonomous-with-guard.** Likely clear, but if the agent hits a fork (two reasonable approaches, missing files, ambiguous YAML), it writes findings to the task entry and *stops* — does not pick.
   - 🔴 **Manual-only.** Needs eyes, design, balance feel, anything subjective.
3. **`TRIAGE <queue>` recipe.** When the user says "triage the queue", the agent:
   - Reads every entry.
   - Re-tiers based on current state.
   - Topo-sorts by `Depends on`.
   - Outputs a recommended overnight plan as a numbered list.
4. **Append rule.** Whenever the agent finds a bug *while* working on something else, if the bug is well-scoped and has a clear repro, it appends a 🟢 entry. Otherwise → `BACKLOG.md` as today.

**Ship criterion.** Going into the first overnight run, there are at least 5 🟢 entries in the queue and the user trusts the tier label.

**Files touched.**
- New `WORKSPACE/automation/autonomous_queue.md`
- New `DOCS/recipes/TRIAGE.md` — *(NB: `TRIAGE` already exists for v1 buckets; this needs a different name or the recipe gets a sub-mode. Suggest **`TRIAGE_QUEUE`** or fold into existing TRIAGE with a flag.)*
- `CLAUDE.md` — append rule + tier vocabulary

**Risk.** Tier inflation. Easy to mark things 🟢 that aren't really. Mitigation: every failed autonomous run, the failing task auto-downgrades 🟢 → 🟡 with the failure note appended.

---

## Phase 5 — Outside-project orchestrator (overnight) — 3-5 evenings

**Problem.** Claude Code sessions time out (30–60 minutes per the brief). One unattended session can do meaningful work; eight back-to-back can do a night's worth. We need a host process *outside* the project that spawns fresh sessions in series, each pointed at the next task.

**Approach.**

This lives in `~/claude-orchestrator/` (or similar — outside the repo, since it controls the repo).

### What it does

1. Reads a **target file** (`night-260514.target.yaml`) listing the queue order to attempt.
2. For each task:
   - Creates a git branch `auto/<task-slug>-<date>`.
   - Spawns a fresh Claude Code session via the CLI in non-interactive mode (`claude -p` or equivalent for headless prompts).
   - First prompt: a constructed brief that includes the task's plan doc, the exit criteria, and an explicit "you have one session, no human is here, commit only inside this branch, if you hit a 🟡 fork stop and write findings to `WORKSPACE/automation/night_log_<date>.md`".
   - Lets the session run to natural completion (or timeout).
   - Captures stdout/stderr to `~/claude-orchestrator/logs/<date>/<task>.log`.
3. After each task: runs `tools/autotest/run-queue.sh` against the test queue.
4. On test failure: marks the task `FAILED` in the night log, **does not merge**, continues to next.
5. On success: marks `OK`, continues.
6. **Never merges to main.** All branches stay open for the user to review in the morning.
7. Sends a **completion notification** (Phase 6).

### Choices to make before building

- **Language.** PowerShell script (Windows-native, no install) vs Python (cross-platform, more libraries). **Recommend PowerShell** since the host is Windows 11 and PS handles Claude CLI invocation, branch management, and JSON parsing fine.
- **Claude CLI invocation mode.** Need to confirm whether `claude --print "prompt"` is reliable for multi-turn autonomous work, or whether each task gets a fresh session via `claude code` headless. Likely `claude -p "<brief>"` per task. **Open: verify with a Claude Code expert before Phase 5 build.**
- **Concurrency.** Strictly serial overnight. Parallel would double GPU burn for tests and create branch chaos. Possible future: a 2-wide pool with strict file-area separation (one agent in `engine/`, one in `mods/`).
- **Budget.** Hard cap on session count (e.g., 12 tasks/night) and total wall-clock (e.g., 9 hours). Orchestrator stops when either is hit.

### Files

- `~/claude-orchestrator/orchestrator.ps1` — main loop
- `~/claude-orchestrator/target-template.yaml` — schema reference
- `~/claude-orchestrator/README.md` — operator manual
- Plus inside the repo: `WORKSPACE/automation/night_log_<date>.md` written by tasks themselves, `WORKSPACE/automation/orchestrator_status.json` updated as a status beacon

**Ship criterion.** One successful overnight run that lands at least one 🟢 task and writes a non-empty `night_log` with verdicts for every attempted task.

**Risk.** A misbehaving session could push to remote, force-push, or `rm -rf`. Mitigations: orchestrator launches with a constrained permission file that disables `git push`, network MCP tools, and anything destructive. Tasks operate only on `auto/*` branches. Pre-flight check: `git status` clean and on `main` before spawning each session.

**Won't do here.** Cloud orchestration (running this on a VPS). The user's machine running overnight is fine for v1. Cloud is a Phase-7+ consideration.

---

## Phase 6 — Notifications & morning briefing — 1 evening

**Problem.** During the night, if the orchestrator hits a real blocker (compile error chain, all tasks fail, machine OOM), the user should know quickly enough to intervene. In the morning, they need a one-page report — not 12 separate logs.

**Approach.**

### Notifications

Three channels, picked at startup:

- **Windows toast** — `BurntToast` PowerShell module, zero-dependency local. Default.
- **ntfy.sh** — free push-to-phone, one-liner POST. For "I want to know on my phone".
- **Email** — fallback. Gmail via SMTP, app password from `.env`.

Trigger conditions:

- **Soft toast** on each task completion (silent during sleep).
- **Loud toast + ntfy** on: 3 consecutive failures, build broken, orchestrator exits non-zero, all tasks failed.
- **None** on routine success.

### Morning briefing

Auto-written by orchestrator on exit to `WORKSPACE/automation/morning_<date>.md`. Format:

```
# Morning Briefing — 2026-05-14

## TL;DR
3 of 5 tasks landed, 1 needs eyes, 1 failed. Branches: auto/foo-260514 (ok),
auto/bar-260514 (ok), auto/baz-260514 (FAILED, see below).

## Per-task
- 🟢 foo: clean, 3 tests green, commit abc1234, ready to merge
- 🟢 bar: clean, 1 test green, commit def5678, ready to merge
- 🟡 qux: stopped at fork — needs your call (see WORKSPACE/automation/night_log_260514.md §qux)
- 🔴 baz: tests failed — see auto/baz-260514 HEAD verdict

## Suggested next manual step
Review auto/foo-260514 first (smallest, clean). Then auto/bar-260514.
Skim qux fork findings before deciding on it.
```

**Ship criterion.** User wakes up, reads one file, knows exactly what to do next without skimming `git log`.

**Files.**
- `~/claude-orchestrator/notify.ps1` — toast / ntfy / email helpers
- `~/claude-orchestrator/briefing.ps1` — synthesizes morning_*.md from logs and verdicts

---

## Phase 7 — Multi-tab agent status board — 1-2 evenings

**Problem.** Even outside overnight, the user often runs 2-3 agents in parallel (one fixing a bug, one doing balance work, one investigating). Today there's no aggregate view; the user has to remember which tab is doing what.

**Approach.**

A status beacon every agent writes on every turn end:

- File: `WORKSPACE/automation/agents/<session-id>.json`
- Schema: `{ "task": "...", "last_action": "...", "status": "active|waiting|done", "files_touched": [...], "tests_queued": [...], "started_at": "...", "last_seen_at": "..." }`
- A simple `tools/automation/status.ps1` reads all `agents/*.json` and prints a one-line-per-agent dashboard.
- Old beacons (>24h) auto-pruned.

Convention added to CLAUDE.md: at the start of every session, write the beacon. On end-of-message, update `last_seen_at`. On FINALIZE, set `status: done`.

**Ship criterion.** User runs `status.ps1` and sees: "3 agents active — A on test-pathfinding, B on sprite-engineer-rifleman, C waiting on user input."

**Won't do here.** A live TUI / web dashboard — overkill. JSON files + one CLI command is enough.

---

## Phase 8 — Test reliability tracking (flake detection) — 2 evenings

**Problem.** Some autotests are deterministic; some flake at the 1–5% level (timing races, RNG seeds). When a flake fails one of 7 batch runs, it produces a false alarm. We need flake metadata per test.

**Approach.**
1. Every run-test invocation logs to `~/.ww3mod-tests/history.jsonl` (one line per run: test, verdict, ts, commit).
2. A `tools/autotest/flake-stats.ps1` reads the last 30 days, prints per-test pass-rate, flags tests below 95%.
3. `run-batch.sh` knows the flake rate per test; on a flaky test fail, automatically reruns it once before declaring fail.
4. Tests with pass-rate <80% get auto-quarantined to a `flaky/` subdir until manually re-graded.

**Ship criterion.** False-alarm batch failures drop to near zero.

---

## Phase 9 — Cloud / always-on orchestrator — speculative, post v1

**Idea.** Move the orchestrator off the user's machine onto a small VPS or a home Mac mini. Reads queue files via a git pull, drives the same Claude CLI, pushes branches back. Frees the local machine entirely.

**Decision: defer until Phases 0–8 prove their value.** Document the idea here, do not build.

---

## Sequencing rationale

- **Phase 0** unblocks daily life *right now* (focus steal).
- **Phase 1** brings consistency without new infra.
- **Phase 2** unlocks Phase 5 (batch runs need to be batchable).
- **Phase 3** is independent of 4/5/6, can slip earlier or later. Worth doing alongside 1/2 because it makes visual changes vastly faster.
- **Phase 4** is a prerequisite for Phase 5. Cannot start overnight runs without a vetted queue.
- **Phase 5** is the big unlock. Must come after 0 (focus), 1/2 (test lanes work in batch), 4 (queue).
- **Phase 6** ships with 5. The orchestrator is half-useless without a briefing.
- **Phase 7** is parallel to everything else. Could ship anytime after Phase 1.
- **Phase 8** is a tax-collector phase — boring, but Phase 5's signal quality depends on it.
- **Phase 9** is north-star aspirational, not a near-term goal.

---

## What's out of scope (deliberate non-goals)

- **Golden-image pixel diffs.** Decided non-goal per SCREENSHOT recipe — semantic checks only. Don't try to compare PNGs at the pixel level.
- **Parallel autonomous sessions.** Strictly serial overnight. Two agents fighting in `engine/` is a recipe for branch hell.
- **Auto-merge to main.** No autonomous commit lands on `main` without user review. Branches only.
- **Auto-discovery of test relevance from diffs.** Tagging is manual (Phase 2). Tried-and-true beats clever-and-fragile.
- **A live web dashboard.** JSON status files + a CLI command (Phase 7) is the cap.

---

## Open questions for the user

1. **Phase 0 strategy preference** — off-screen-position, NoActivate-shim, headless-lite, or fix-and-keep `--minimized`? Recommend off-screen first, fall back to fix-minimized if it doesn't pan out.
2. **Notification channel for Phase 6** — toast only, or set up ntfy.sh now for phone alerts?
3. **Overnight budget** — what's the upper bound on hours and tasks per night? Default proposed: 9 hours, 12 tasks.
4. **Trust calibration** — should the first overnight run be deliberately small (2 🟢 tasks) before scaling up? Strongly recommend yes.
5. **Orchestrator host language** — confirm PowerShell, or prefer Python?

---

## Files this folder will eventually contain

Once phases ship, expect:

- `README.md` (this file — living plan)
- `autonomous_queue.md` (Phase 4)
- `night_log_YYMMDD.md` (Phase 5, per-night)
- `morning_YYMMDD.md` (Phase 6, per-night)
- `orchestrator_status.json` (Phase 5/7)
- `agents/<session-id>.json` (Phase 7)

External (outside repo) once Phase 5 ships:

- `~/claude-orchestrator/orchestrator.ps1`
- `~/claude-orchestrator/notify.ps1`
- `~/claude-orchestrator/briefing.ps1`
- `~/claude-orchestrator/logs/YYMMDD/`
