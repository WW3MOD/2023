# Cohesion workspace

Documentation and planning for WW3MOD's grouped-unit movement system — the layer between "player issues a Move/AttackMove order on a group of units" and "each unit gets a per-actor destination". The user-visible promise is *"click where you want, the squad arranges itself sensibly relative to terrain"*; the implementation is `CohesionMoveModifier` + `IModifyGroupOrder` + `Map.DensityLayer` + `CohesionSlotMemory`.

This workspace exists because the cover-aware behavior was shipped but felt wrong in playtest — the symptom was "same as the old box formation, just with extra steps". An autotest-driven diagnosis on 2026-05-13 found three concrete root causes (recorded in `DISCOVERIES.md`), all of which were fixed. The system is now at a *functional* baseline but has several visible gaps before it's *good*. These docs lay out the current state, name the remaining problems, and propose directions.

## Docs

Read in order:

- [`01_cohesion_as_built.md`](01_cohesion_as_built.md) — the implementation as it stands today. Order-pipeline hook, density signal source, the four intents and their slot bidders, the leash trait, the cohesion-mode toggle, the scatter hotkey, the test surface. Files, line refs, YAML knobs, recent fixes.
- [`02_problem_statement.md`](02_problem_statement.md) — what we want users (and bots) to observe, what we have today, the gaps between them, success criteria. No solutions in here — only diagnosis.
- [`03_design_directions.md`](03_design_directions.md) — speculative paths forward for each gap. Visualization, per-stance leash budgets, per-unit-type role profiles, garrison intent, attack semantics. Trade-offs, open questions, suggested migration order.

## Archive

`archive/` holds prior plans and notes. Reference only — not authoritative going forward.

- [`260512_intent_aware_movement.md`](archive/260512_intent_aware_movement.md) — the original design plan. Phase structure (1: wiring, 2: density field, 3: interpreter, 4: leash, 5: visualization, 6: attack semantics). Phases 1–4 mostly landed; 5–6 still pending. The "modular cover signal API" idea is preserved in this doc but not yet implemented — only `Map.DensityLayer` is wired.

## What changed recently

Three fixes landed on 2026-05-13 after the autotest diagnosis (commit `657d94ad`):

1. `Approach` walks click → group (not group → click), so squads spawn-camping next to cover don't anchor slots one step out instead of marching to far clicks.
2. `EdgeLine` (and `Approach`'s perpendicular line) use a CoverScore-aware per-slot neighborhood search — units actually land behind trunks instead of in a dead-straight geometric column.
3. SpreadInside band widened (`EdgeOffsetThresholdCellsSq` 2 → 9) so clicks within ~3 cells of cover centroid stay SpreadInside instead of being routed to a perpendicular EdgeLine that visually read as the legacy box.

The diagnostic `Log.Write` at the bottom of `ModifyGroupOrder` is still on; it logs one line per grouped order, useful for live playtest observation. Strip when the feel is dialed.
