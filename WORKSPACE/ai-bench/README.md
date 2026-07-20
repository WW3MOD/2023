# AI Benchmark — side project

An autonomous system that **improves the Experimental AI** (`ModularBot@experimental`)
against a ladder of standardized benchmark scenarios, driven by a Maestro manager
on autoburn. The Normal AI is the fixed yardstick; progress merges to `main`
early and often so the user can play it.

> **Terminology (2026-07-20):** the dev bot was renamed `@v2` → `@experimental`
> ("Experimental AI"); a frozen `ModularBot@stable` ("Stable AI") now holds the last
> validated snapshot (promotion policy: [`SPEC.md`](SPEC.md) §13). Historical "v2"
> names in `runs/` are left as written.

## The three documents

| File | What it is | When to read |
|---|---|---|
| [`SPEC.md`](SPEC.md) | **The constitution.** The loop, run-policy modes (windowed vs hidden), mutable-scope + anti-cheat rules, worktree/merge protocol, advancement criteria, data recording, failure handling, scaling. A fresh manager bootstraps entirely from this. | Once at bootstrap; reference thereafter. |
| [`LADDER.md`](LADDER.md) | **The tests.** The scenario-ladder protocol + the three River Zeta WW3 scenarios (economy race, force efficiency, win-rate) and the composite gate that clears a rung. | When picking what to run / defining a new scenario. |
| [`REVIEW.md`](REVIEW.md) | **The status board (info-only).** One-minute review: *Needs attention* (what wants a human) → *Highlights* (recent milestones) → *Current state* (live ladder + SHA) → *Activity log* (full history). One-way — the loop writes, you read; steering happens on the **Maestro dashboard**, not in the file. | The manager writes it **every cycle**; the user reads it to review, and steers via the dashboard. |

`runs/` holds the committed **cycle cards** (one distilled JSON per
hypothesis-batch, SPEC §8.3). Bulky raw match data lives under
`tools/autotest/tournament-results/` (git-ignored, harness-owned).

## The loop in one line

> pick a hypothesis → implement it in the `ai-bench` worktree → run a benchmark
> batch → score vs the Normal control → log a cycle card → merge-or-revert.

## Hard invariants (see SPEC §4, §11)

- **Never push to remote** (the user pushes). Merges are local.
- **Improve the AI, never shorten the yardstick** — no unit-stat / balance edits,
  no game-rule changes to fit the benchmark. Engine fixes that *unblock* the AI
  are fine.
- **Control AIs (Normal/Rush/Turtle) stay behaviorally byte-identical.**
- **A crash is the only unacceptable merge.**

## Status

Bootstrapped 2026-07-19 (docs only — no code, no runs yet). The hidden-window
substrate is verified, so the loop runs **unsupervised (Mode B)** from the start
— no user run window needed (SPEC §3). First action: read REVIEW.md + check the
Maestro dashboard for any live user directions, create the worktree, do the §3.3
bootstrap smoke run (proves the pipeline + the Windows-portability items), then
baseline Scenario 1.
