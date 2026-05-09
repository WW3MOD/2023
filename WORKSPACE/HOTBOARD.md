# Hotboard

> What's actively in motion **right now**. The v1 release tracker (`RELEASE_V1.md`) is the source of truth for scope and status.
> Cap ~15 lines. Rotate stale entries out — once shipped or `[T]`, the tracker tells the story.

## Working on
- **Crew evacuation overhaul (260509)** — staged: hatch-emerge → walk-away → prone-when-wounded; cookoff = FireDeath; helis refuse safe-land on blocked cells. Awaiting playtest. Plan: `WORKSPACE/archive/plans/260507_crew_evac_plan.md`
- **Pathfinding friendly-blocker scope (260506)** — vehicle groups drop moves on long routes / narrow gaps. Briefing: `WORKSPACE/plans/260506_pathfinding_friendly_blockers.md`. Not started

## Recent Wins (last 5)
- **Crew = real soldiers** — hatch-emerge animation, walk away, prone when wounded, cookoff = FireDeath, blocked-cell heli land = unsafe (5 commits)
- **Tier 1 batch 260509** — crew instakill scaling, SR own-click regression, Iskander/HIMARS shockwave radius, parallel-queue false-alarm cleared (5 commits)
- **Test harness Tier 2** — auto-asserting Lua, batch runner, test discovery, Lua errors auto-fail (5+ commits)
- **Supply & ammo economy overhaul** — refund-on-evacuate, LC docking gate, auto-tooltip weapon block, 63-pool tier balance (15 commits)
- **Setup/aim phase + DR auto-fire stabilization** — HoldFireWhileMoving, SetupTicks, RequiresForceFire, NoSelfDefenseInterrupt (3 commits)

## Quick Stats
- Engine files modified: 280+
- Maps: 13
- AI bot types: 3 (Normal, Rush, Turtle)
