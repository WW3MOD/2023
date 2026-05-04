# Hotboard

> What's actively in motion right now. The v1 release tracker (`RELEASE_V1.md`) is the source of truth for scope and status.

## Working on
- **Garrison stabilization Round 1 (260504)** — items 3, 4, 5A, 5B, 6 shipped + Z-blink fix from 260504 playtest; awaiting verification. Spec: `CLAUDE/plans/260504_garrison_stabilization_design.md`.
- **Triaged 260504 playtest** — TECN-panic-loses-order, heli-edge-evac-cheese, shift+G→move, heli-formation-flock all in `RELEASE_V1.md`.

## Recent Wins (last 5)
- **Garrison teamleader Z-blink fix** — `AttackGarrisoned.DoGarrisonedAttack` now clamps Z to terrain like `GarrisonManager.Tick` does
- **Garrison stabilization Round 1** — Stop order, raw IMove, pre-entry pause, skip-ahead, hysteresis+sticky-targets (5 commits)
- **Garrison overhaul Phases 1–6** — indestructible buildings, dynamic ownership, directional targeting, suppression integration, visuals
- **Cargo system Phases 2A–E** — CargoSupply, TRUK→pure transport, cargo panel, mark+unload
- **Helicopter crash + crew overhaul** — VehicleCrew on all helis, two-tier emergency landing, capture-by-pilot
- **Stance rework Phases 1–4** — modifiers, tooltips, resupply, cohesion, patrol

## Quick Stats
- Engine files modified: 280+
- Maps: 13
- AI bot types: 3 (Normal, Rush, Turtle)
