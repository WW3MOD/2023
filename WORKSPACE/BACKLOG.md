# Backlog

> Deferred tasks, ideas, and parking lot items.
> `[ ]` pending | `[x]` done | `[dropped]` irrelevant
> v1 items live in `RELEASE_V1.md` — this file is for v1.1+ and parking-lot ideas only.

## Deferred Tasks
- [ ] **Lobby Phase 6 — inline map browse** — designed but deferred. Currently `CHANGEMAP_BUTTON` opens `MAPCHOOSER_PANEL` as a 900×600 modal; plan calls for inline swap inside the TL panel body. Plan: `WORKSPACE/lobby/IMPLEMENTATION_PLAN.md#phase-6`. Requires new MAPCHOOSER_INLINE widget (copy of MAPCHOOSER_PANEL but sized to TL bounds, no modal lifecycle), C# tab-swap glue between MAP_PREVIEW_ROOT and a sibling MAP_BROWSE_ROOT, and re-targeting MapChooserLogic args. Functionally the existing modal works — the upgrade is purely visual polish. *v1.1*
- [ ] **Lobby handicap access path** — V5 row dropped Handicap; column parked at X:-200 W:1 H:1 in lobby-players.yaml. Decide path (context-menu / expandable detail / spawn-cell overload / drop entirely) once usage telemetry exists. See `DISCOVERIES.md` 2026-05-18. *v1.1*
- [ ] **Lobby 1366×768 bottom-strip overflow** — at 1366 wide the setup-row buttons overflow into SPECTATE_AREA by ~80px. Phase 11 tightened widths to give 1920×1080 headroom; 1366 still cramped. Fix only if a player reports it. *v1.1*
- [ ] **Flashing pips honour real-time, not game-speed** — out-of-ammo pip blinks on game ticks; on fast-forward they strobe, on slow they crawl. Drive flash from wall-clock instead. *Reported 260503*
- [ ] Per-Supply-Route production queues (requires engine changes)
- [ ] Per-unit rot sprites (bleedout uses generic e1 frames)
- [ ] Group Scatter polish (mixed unit types, UI feedback)
- [ ] Cherry-pick useful parts from skane/xavi branches
- [ ] Extract useful maps from maps branch
- [ ] Clean up stale branches (bypass, counterbattery, speed)

## Ideas Parking Lot
- [ ] Engine upgrade to release-20250330 (12-22 sessions, defer until gameplay done)
- [ ] Ukraine as third faction
- [ ] Ammo costs money — full economy rework (separate from current SupplyValue tier work)

## Completed
