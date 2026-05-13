# AI workspace

Start-from-scratch documentation and planning for WW3MOD's AI.

The previous attempt (`v2` — InfluenceMap + LayeredDefence + MountedTransport + CaptureCoordinator) hit a structural wall: independent `IBotTick` modules using `IsIdle` as their only "is this unit available?" signal, fighting each other for unit control with ad-hoc reservation handshakes. We're starting over.

The InfluenceMap, FrontlineOverlay, doctrine docs, capture-rules cleanup, autotest harness, and the `enable-ai-v2` condition system are all keepable — they're cited in the new design but the coordination layer is being redrawn.

## Docs

Read in order:

- [`01_default_ai_explained.md`](01_default_ai_explained.md) — how the engine's stock `ModularBot` thinks, every module's responsibilities, all the injection points, and the architectural assumptions worth flagging before we redesign.
- [`02_problem_statement.md`](02_problem_statement.md) — eight observable behaviors we want, three layers of what we have today, five root-cause gaps, non-negotiables / non-goals, eight success criteria.
- [`03_substrate.md`](03_substrate.md) — the plumbing layer. Five tiers (engine → shared perception → per-bot state → observability → brain hook). Shared world traits (`ResourceMap`, `TerrainCache`, `SectorMap`), per-bot stores (`GoalLedger`, `ClaimRegistry`, `SectorBudget`, `ProductionPlan`, `Memory`), and the debug/overlay channels. Speculative — not yet binding. Open questions at the end for pushback.

Suggested next: `04_brain.md` — the decision pipeline (perceive → plan → assign → dispatch → react), goal-to-order translation, replan triggers. See `03_substrate.md` §13 for rationale.

## Archive

`archive/` holds the prior v2 design docs, doctrine, handoff notes, stage docs, sanity findings, tournament workflow, PITFALLS. **Reference only — not authoritative going forward.** Useful for "what did we try, what didn't work, what's the historical context".
