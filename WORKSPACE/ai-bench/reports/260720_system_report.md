# Autonomous Bot Development — Full System Report

*2026-07-20 · main @ 43441501 · loop running under autoburn*

## 1. Mission

- **Two goals, one list:** bots that *win* and bots that *read like a real modern battlefield* (doctrine-grounded, no game tropes) — `DOCS/design/ai-realism.md`. The realism priorities and the win-rate priorities turned out to be the same priorities.
- **Aim-high mandate (user, today):** don't just extend the inherited OpenRA botmodule pattern — keep a standing plan for structural leaps (§6).

## 2. The bot under development — Experimental AI

The strategic layer, built this project (engine traits, YAML-tuned):

- **PoiMap** — scores every point of interest: `value × distance-falloff × threat-discount`. Values per POI class (derricks, logistics, supply routes) in `world.yaml`.
- **Score-floating offense axes** — top-4 targets become attack axes each re-eval; no scripted phases, targets compete by score. Biases: income-secure 150, enemy-attack 80, SR-deny 120.
- **Goal-guard ledger** — reserves units per goal (capture squads, garrisons) so axes can't strip them.
- **Capture coordinator** — routes the TECN engineer to capture targets (current weak point: single-capturer fragility, §5).
- **Garrison module** — 1–3 defenders per held money-POI, value-ramped.
- **Mounted transport** — infantry ride vehicles toward distant goals.
- **NEW, parked on branch:** dispersion cohesion — *spread to move, mass to assault* (`exp-dispersion` @ e51e1c3f, awaiting its verify cycle).

**Bot roster:** `@experimental` (the loop's working bot) · `@stable` (frozen snapshot, promotion per SPEC §13) · Normal / Rush / Turtle (frozen controls — never touched, they are the measuring sticks).

## 3. The measuring instrument — benchmark substrate

- **SPEC.md** — protocol: hidden Mode-B runs (bot can't see it's being tested), one behavior change per cycle, eager merge, promotion policy.
- **LADDER.md** — scenario rungs + pass bars. Active rung: **S1 economic** (River Zeta, 12 neutral derricks, 7500-tick window, primary + mirror + Normal-vs-Normal calibration map).
- **REVIEW.md** — the user's follow-along surface: milestones, questions, inbox annotations.
- **Verdict pipeline (v4)** — `BotVsBotMatchWatcher` + `WeightedComponentMatchScorer`; `capture_income` reads a **gross income integral** (observer-only trait), so holding a derrick counts even after spending. Unit-test-pinned.
- **Run artifacts** — JSON verdicts + analysis docs in `runs/`; `tools/autotest/parse-s1-batch.py` aggregates batches.
- **Scenario tooling** — `run-test.sh` (single), batch/tournament scripts; **one game process machine-wide** (the run slot).

## 4. The factory — orchestration loop (Maestro)

- **Manager (this session)** routes cycles, merges, keeps REVIEW/LADDER honest, asks assumption-questions the user can override asynchronously.
- **Workers** — disposable Claude agents, one focused task each: RECON (read-only design study) / IMPLEMENT+VERIFY / BATCH runner / CURATION. Isolated in git worktrees (`~/worktrees/ww3mod/…`); heavy compute serialized (≤2 concurrent, never build-during-batch — today's memory incident).
- **Merge topology** — feature branches → `main` eagerly when assumed-improved; unverified behavior stays on its branch until its own benchmark run.
- **Knowledge flow** — workers capture insights to `WORKSPACE/DISCOVERIES.md`; curation passes verify-then-promote into `DOCS/reference/` (curated, trusted, fix-wrong-on-sight). Design studies live in `WORKSPACE/plans/`.

## 5. Where things stand — numbers from today's baseline (N=10 + N=10 calibration)

- **Experimental beats Normal 8–2, symmetric by spawn** → real skill, not spawn luck. S1 discriminates.
- **In-window derrick capture: 4/10** — below the 6/10 bar. Failing runs: flat income curves — the lone TECN never holds a derrick to term. **Cycle 1 = capture-reliability** (recon in flight); SR-contestation plan parked, implement-ready.
- **Map fairness:** mild russia-side lean (~28% score), neutralized by the mandatory mirror. No fix needed.
- **Pass bar reformed** (old one was degenerate 0≥0): *capture ≥ 6/10 AND conditional gross median ≥ $5,000* — provisionally adopted, flagged for ratification.
- **Known limits:** unseeded `LocalRandom` (no run determinism yet) · single run slot · autotests steal window focus.

## 6. Aiming higher — the structural roadmap

Ordered; each is a RETHINK-gated leap beyond botmodule patching:

1. **Mission abstraction** — capture/assault/garrison as first-class *missions* (objective + assigned forces + escort + retry + abort criteria) instead of per-module order spam. Cycle 1's capture-reliability recon is explicitly evaluating whether to start here.
2. **Operations layer / phase state machine** — BUILDUP → PROBE → OFFENSIVE → CONSOLIDATE; fixes the diagnosed core weakness ("goals but no operations" — continuous penny-packet commitment, no massing).
3. **Reinforcement packaging** — call-ins arrive as combined-arms packages tied to missions, not à-la-carte units.
4. **Strategic blackboard** — pull strategy out of the per-tick OpenRA trait pattern into one decision layer over a world-model snapshot; modules become executors.
5. **Self-tuning** — after seeded determinism lands: automated parameter search (YAML weights) against the ladder, overnight batches.
6. **Telemetry-driven diagnosis** — structured per-match event logs (capture markers, clumpRadius, axis lifecycles) so failing runs classify themselves; `parse-s1-batch.py` grows into the analyzer.
7. **Ladder growth** — S2 contested-mid, S3 SR-pressure, full-match win-rate rungs; PROMOTE Stable as rungs are passed.
8. **Doctrine backlog** (realism dossier order): recon-strike loop, force preservation/culmination, defense-in-depth + reserves, fires-first, infiltration.

## 7. The operating routine

Now codified in **`WORKSPACE/ai-bench/DOCTRINE.md`** — nine action types (RECON, IMPLEMENT+VERIFY, BASELINE, CALIBRATE, RETHINK, CURATE, PROMOTE, EXPAND, REPORT) with explicit triggers, e.g.:

- RECON before every cycle (cheap, parallel, survives re-routing).
- BASELINE after any scorer change or every ~5 cycles.
- **RETHINK every ~5 cycles or after 2 consecutive failed bars — where radical options get costed for real.**
- CURATE at ~10 unpromoted discoveries.
- One behavior change per cycle, always; bars ratified via flagged questions, never improvised silently.

## 8. In flight right now

- Capture-reliability RECON (worker, read-only, main tree).
- `exp-dispersion` branch parked pending its verify cycle.
- Queue after cycle 1: dispersion verify → SR-contestation → RETHINK checkpoint (5 cycles will have elapsed — mission abstraction gets its full costing there, or sooner if the recon says now).
