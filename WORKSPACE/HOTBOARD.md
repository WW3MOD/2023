# Hotboard

> What's actively in motion **right now**. The v1 release tracker (`RELEASE_V1.md`) is the source of truth for scope and status.
> Cap ~15 lines. Rotate stale entries out — once shipped or `[T]`, the tracker tells the story.

## Working on
- **AI overhaul foundation (260511)** — survey + 3-layer architecture (Perception/Strategy/Tactics) + 5-phase plan written. Home: `WORKSPACE/ai/`. Awaiting user pass on open questions in `foundation_260511.md §7`
- **AI tournament harness (260511)** — dual `ModularBot@legacy`/`@v2` + headless+parallel match runner + autonomous milestone-driven loop. Lands before any new-brain code so every change is measurable. Plan: `WORKSPACE/plans/260511_ai_tournament_harness.md`. Not started
- **Crew evacuation overhaul (260509)** — staged: hatch-emerge → walk-away → prone-when-wounded; cookoff = FireDeath; helis refuse safe-land on blocked cells. Awaiting playtest. Plan: `WORKSPACE/archive/plans/260507_crew_evac_plan.md`
- **Pathfinding friendly-blocker scope (260506)** — vehicle groups drop moves on long routes / narrow gaps. Briefing: `WORKSPACE/plans/260506_pathfinding_friendly_blockers.md`. Not started

## Recent Wins (last 5)
- **Economy refactor (260511, branch `main`)** — per-batch SupplyValue (CreditValue collapsed), CargoSupply trait ripped (~1100 LOC), TRUK ported to SupplyProvider + new DropsSupplyCache trait, LC drain on truck restock. Spec: `DOCS/reference/economy.md`. 5 commits (`2ab93552` … `7a32e3df`). Awaiting playtest: TRUK deploy/restock/deliver orders, SUPPLYCACHE bar/circle render
- **Balance session (260510, branch `balancing`)** — 9 reusable `test-balance-*` autotests + `WORKSPACE/balancing/260510_balance_recommendations.md`. Key findings: B-01 (no Russian vehicle inherits ^Combatant — latent bug), R-02 dropped (ATGM Pen 100 actually works fine vs MBT top), R-03 (BMP-2 1300 vs Bradley 1500 cost gap unearned), combat-sim hardcoded stats out-of-sync with YAML by 5-15×. Recommendations only — no balance changes applied.
- **Heli→heli missile vanish fixed (260510)** — Apache now one-shots a Mi-28 (was 5-50 graze damage). Three-part fix: (a) `Missile.cs` mid-tick segment-aim-point proximity check (fast missiles no longer fly past target between ticks); (b) airburst gate on `!flyStraight`; (c) Hellfire SpreadDamage `Penetration: 1→20` so Heavy heli armor (Thick 20) doesn't divide damage by 20. Real root cause was the armor-divide on SpreadDamage, not the airburst diagnosis. New autotest `test-heli-vs-heli-missile`; all WGM autotests still green
- **WGM/Hellfire accuracy + tree gating (260510)** — full pass on the wire-guided ATGM bug: density-based fire-time gate (1 tree free, 2-3 small risk, 4+ deny), per-weapon `ClearSightThreshold` so strict weapons aren't washed out, Inaccuracy 768→128, LockOnInaccuracy 0, faster turn rate. 6 autotests including 0..6-tree ladder. Experimental package (uncommitted) in `WORKSPACE/EXPERIMENTAL_NOTES.md`
- **Artillery turret stowed while driving (260509)** — Paladin/GRAD/TOS/M270 lock turret to InitialFacing under `Turreted.RealignWhileMoving`; new `TurretFacing` Lua bind + auto test
- **SR rally per-waypoint order types (260509)** — default/Alt/Ctrl modifiers map to Move/AttackMove/ForceMove; per-segment line color; auto test exercises encoding pipeline
- **Crew = real soldiers** — hatch-emerge animation, walk away, prone when wounded, cookoff = FireDeath, blocked-cell heli land = unsafe (5 commits)

## Quick Stats
- Engine files modified: 280+
- Maps: 13
- AI bot types: 3 (Normal, Rush, Turtle)
