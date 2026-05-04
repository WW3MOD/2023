# Hotboard

> What's actively in motion right now. The v1 release tracker (`RELEASE_V1.md`) is the source of truth for scope and status.

## Working on
- **Supply truck resupply bar + LC refill (260504)** — TRUK now has 3-stance bar (Hold/Auto/Evacuate, default Evacuate); Auto seeks Logistics Center for pip refill, falls through to Evacuate if no LC. Awaiting playtest. Spec/plan: `docs/superpowers/{specs,plans}/2026-05-04-supply-truck-resupply-and-rubble-evac*.md`.
- **Rubble building evac + protection (260504)** — Unload now reachable when only port soldiers remain (was gated by `Cargo.IsEmpty()`); RubbleProtection field at 30% for 1HP rubble; defensive invariant guards on port/shelter to fix double-display. Awaiting playtest.
- **Garrison stabilization Round 1 (260504)** — items 3, 4, 5A, 5B, 6 shipped + Z-blink fix from 260504 playtest; awaiting verification. Spec: `CLAUDE/plans/260504_garrison_stabilization_design.md`.
- **260504 playtest Phase B trio** — TECN-panic stash/resume, heli edge anti-cheese, shift+G attack-ground preservation all shipped (3 commits); awaiting playtest verification.

## Recent Wins (last 5)
- **Supply truck resupply bar + LC refill** — CargoSupply gets 3-stance bar; SupplyProvider extended to refill trucks; default = Evacuate (4 commits)
- **Rubble evac + protection + double-display** — Unload gating fix (real cause: `IsEmpty()` returns true when soldiers at ports), RubbleProtection 30%, invariant guards on shelter pip rendering (4 commits incl. investigation)
- **260504 Phase B trio** — TECN-panic order restore + heli edge-evac missile cheese + shift+G attack-ground preservation (3 commits)
- **Garrison teamleader Z-blink fix** — `AttackGarrisoned.DoGarrisonedAttack` now clamps Z to terrain like `GarrisonManager.Tick` does
- **Garrison stabilization Round 1** — Stop order, raw IMove, pre-entry pause, skip-ahead, hysteresis+sticky-targets (5 commits)

## Quick Stats
- Engine files modified: 280+
- Maps: 13
- AI bot types: 3 (Normal, Rush, Turtle)
