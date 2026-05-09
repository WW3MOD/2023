# Hotboard

> What's actively in motion right now. The v1 release tracker (`RELEASE_V1.md`) is the source of truth for scope and status.

## Working on
- **Supply & ammo economy overhaul (260506)** — Phases 1–3 shipped from `WORKSPACE/plans/260506_supply_ammo_economy.md`. P1: empty-truck refund deduction, LC `unit.docked` gate, Ctrl+click = deliver vs default repair+refill, new `RefillFromHost` activity + Restock order. P2: `IProvideTooltipDescription` interface + auto weapon block in production tooltip (AmmoPool feeds it; renderer adds grand-total for 2+ pools). P3: tier table applied to ~63 active AmmoPools across 9 YAMLs. Awaiting playtest.
- **Setup/aim phase + DR auto-fire stabilization (260506)** — `HoldFireWhileMoving`, `SetupTicks`+`SetupCondition`, `RequiresForceFire`, `NoSelfDefenseInterrupt` shipped; DroneTargeter/HIMARS/Iskander no longer auto-fire on enemy actors; artillery now stop+setup+aim+fire. Awaiting playtest. Plan: `WORKSPACE/plans/260506_setup_aim_phase.md`.
- **Pathfinding friendly-blocker scope (260506)** — User reports vehicle groups dropping moves on long routes / narrow gaps; pathfinder treats friendlies as walls. Briefing for new chat: `WORKSPACE/plans/260506_pathfinding_friendly_blockers.md`.
- **Rubble building evac + protection (260504)** — Unload now reachable when only port soldiers remain; RubbleProtection 30% for 1HP rubble. Awaiting playtest.
- **Garrison stabilization Round 1 (260504)** — items 3, 4, 5A, 5B, 6 shipped + Z-blink fix; awaiting verification.

## Recent Wins (last 5)
- **Supply & ammo economy overhaul** — refund-on-evacuate fix, LC docking gate, Restock order + `RefillFromHost` activity, auto-tooltip weapon block, ~63-pool tier balance pass (15 commits across P1/P2/P3)
- **Setup/aim phase + DR auto-fire stabilization** — 6 fixes in one session: HoldFireWhileMoving, SetupTicks, RequiresForceFire (DR root cause), NoSelfDefenseInterrupt, AimingDelay bumps, cursor-truthfulness, dead-field cleanup, WAngle docs reconcile (3 commits)
- **Supply truck resupply bar + LC refill** — CargoSupply gets 3-stance bar; SupplyProvider extended to refill trucks; default = Evacuate (4 commits)
- **Rubble evac + protection + double-display** — Unload gating fix, RubbleProtection 30%, invariant guards on shelter pip rendering (4 commits incl. investigation)
- **260504 Phase B trio** — TECN-panic order restore + heli edge-evac missile cheese + shift+G attack-ground preservation (3 commits)

## Quick Stats
- Engine files modified: 280+
- Maps: 13
- AI bot types: 3 (Normal, Rush, Turtle)
