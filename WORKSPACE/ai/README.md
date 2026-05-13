# AI workspace

Start-from-scratch documentation and planning for WW3MOD's AI.

The previous attempt (`v2` — InfluenceMap + LayeredDefence + MountedTransport + CaptureCoordinator) hit a structural wall: independent `IBotTick` modules using `IsIdle` as their only "is this unit available?" signal, fighting each other for unit control with ad-hoc reservation handshakes. We're starting over.

The InfluenceMap, FrontlineOverlay, doctrine docs, capture-rules cleanup, autotest harness, and the `enable-ai-v2` condition system are all keepable — they're cited in the new design but the coordination layer is being redrawn.

## Docs

- [`01_default_ai_explained.md`](01_default_ai_explained.md) — how the engine's stock `ModularBot` thinks, every module's responsibilities, all the injection points, and the architectural assumptions worth flagging before we redesign.

Future docs (planned, not yet written):
- `02_problem_statement.md` — what we want, what we have, the gap
- `03_design.md` — the new brain architecture, decision-flow, data model
- `04_migration_plan.md` — how we ship without breaking the legacy AI

## Archive

`archive/` holds the prior v2 design docs, doctrine, handoff notes, stage docs, sanity findings, tournament workflow, PITFALLS. **Reference only — not authoritative going forward.** Useful for "what did we try, what didn't work, what's the historical context".
