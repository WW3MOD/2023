# Post-Upstream-Merge Audit Findings
**Date:** 2026-04-02
**Scope:** OpenRA release-20250330 merge into WW3MOD

## Fixed Issues (Already Applied)

### 1. Missile ContrailWidth YAML silently ignored
- **Cause:** Upstream renamed `ContrailWidth` to `ContrailStartWidth`/`ContrailEndWidth` in Missile.cs
- **Impact:** 3 missile weapons (WGM, MANPAD, Stinger) had invisible contrail width settings
- **Fix:** Renamed all YAML entries to `ContrailStartWidth`

### 2. Bullet.cs contrail API inconsistency
- **Cause:** Bullet.cs still used old single `ContrailWidth` while Missile.cs uses start/end
- **Fix:** Updated Bullet.cs to match Missile.cs pattern (`ContrailStartWidth`/`ContrailEndWidth`), updated all 16 YAML entries in weapons-ballistics.yaml

## Items for User Review (Not Fixed — Need Your Decision)

### 3. Weapon IsValidAgainst() no longer checks warheads (UPSTREAM CHANGE)
- **What changed:** `WeaponInfo.IsValidAgainst(Actor, Actor)` used to iterate through all warheads and return true only if at least one warhead was valid against the target. Now it ONLY checks weapon-level target types.
- **Practical impact:** A unit might aim at a target where the weapon's target types match but no warhead can actually damage it. The warhead still validates on impact (so no phantom damage), but the unit wastes a shot. This is most likely to matter for weapons with narrow warhead ValidTargets that differ from weapon ValidTargets.
- **WW3MOD risk:** LOW — most weapons have matching weapon/warhead ValidTargets. Suppression warheads use GrantExternalCondition (no IsValidAgainst override).
- **Recommendation:** Monitor during playtesting. If units waste shots on wrong targets, we can add a custom check back.

### 4. FrozenActor.Actor now returns null for dead actors
- **What changed:** Previously returned the actor object even if dead. Now returns null when `BackingActor.IsDead`.
- **Impact:** All existing code already null-checks `FrozenActor.Actor`, so this is safe. But it changes semantics — code that relied on accessing dead backing actors through frozen layer will now get null.
- **Recommendation:** No action needed. All callsites already null-safe.

### 5. Capturable.ValidRelationships removed (silently ignored in YAML)
- **Files:** `husks-vehicles.yaml` (line 22), `structures.yaml` (lines 149, 152)
- **What changed:** Capturable trait no longer has ValidRelationships field. Relationship checks now handled entirely by the Captures trait on the capturing unit + CaptureType matching.
- **Impact:** NONE in practice — the CaptureType system (`building-neutral`, `building-occupied`, `husk`) already provides the same filtering. The removed field was redundant.
- **Recommendation:** Clean up the YAML by removing the dead `ValidRelationships` lines (cosmetic only).

### 6. Infiltrates.PlayerExperience removed (silently ignored)
- **Files:** `disable-player-experience.yaml` (lines 15, 25), `infantry.yaml` (line 1849/867/873)
- **What changed:** Infiltrates and Captures traits no longer have PlayerExperience field.
- **Impact:** NONE — all values were set to 0 (disabling the feature), and the feature itself was removed.
- **Recommendation:** Clean up the YAML by removing dead entries (cosmetic only).

### 7. ClosestToIgnoringPath() replaces PositionClosestTo() for missile targeting
- **What changed:** Missiles now use `ClosestToIgnoringPath()` to pick which target position to aim at, ignoring pathfinding obstacles.
- **Impact:** Missiles might aim at target positions that are behind obstacles, though this is unlikely to matter for air-to-ground missiles. Could matter for ground-launched missiles in urban maps.
- **Recommendation:** Monitor during playtesting with missiles in urban/obstructed terrain.

### 8. Pathfinding heuristic refinement
- **What changed:** Pathfinding now validates reachability in reverse searches and uses improved heuristic signatures.
- **Impact:** Units might choose slightly different paths. Should be improvements but could feel "different."
- **Recommendation:** Monitor during playtesting. Paths should be equal or better.

## Systems Verified Clean (No Issues Found)

| System | Status |
|--------|--------|
| Aircraft velocity movement (CanSlide) | OK — all custom code intact |
| Fly.cs CanSlide separation | OK — never calls FlyTick for helicopters |
| Land.cs / FlyIdle.cs / TakeOff.cs | OK — no double movement |
| Mobile.cs (reverse, mid-cell redirect) | OK — all custom fields present |
| HeliEmergencyLanding | OK — full state machine intact |
| AutoTarget (engagement stances) | OK — all interfaces implemented |
| Armament.cs (multi-weapon, ammo) | OK — custom fields preserved |
| AmmoPool.cs (SupplyValue, resupply) | OK — extended mechanics intact |
| DamageWarhead.cs (penetration, directional) | OK — all custom fields present |
| SpreadDamageWarhead.cs | OK — falloff system working |
| ShockwaveDamageWarhead.cs | OK — custom WW3MOD warhead intact |
| Health.cs (50% critical threshold) | OK — damage states correct |
| AttackBase.cs (retargeting) | OK — critical health switching works |
| Suppression system | OK — ExternalCondition + InfantryStates working |
| ProductionQueue / ProductionFromMapEdge | OK — IProductionSpeedModifier accumulator works |
| SupplyRouteContestation | OK — graduated bar, production slowdown |
| Cargo.cs (garrison integration) | OK — all methods correct |
| GarrisonManager | OK — deploy/recall system intact |
| MapLayers (was Shroud) | OK — vision layers working |
| Passable (was Crushable) | OK — obstacle interaction preserved |
| SmartMove | OK — IWrapMove + selective firing |
| VehicleCrew / CrewMember / EnterAsCrew | OK — crew system intact |
| HealerClaimLayer / HealerAutoTarget | OK — medic targeting working |
| UnitDefaultsManager | OK — per-type defaults persist |
| CohesionMoveModifier | OK — group formation working |
| PatrolOrderGenerator / Patrol | OK — waypoint loop system working |
| Detectable / BlocksSight / Radar | OK — vision traits intact |
| CommandBarLogic (4 stance bars) | OK — widget APIs correct |
| CargoPanelLogic | OK — panel controls functional |
| MiniMapWidget | OK — renamed from RadarWidget, working |
| Condition system | OK — grant/revoke/require all working |
| Explodes trait | OK — old class still fully functional |
| EngineerRepairable | OK — old class still fully functional |
| MenuPaletteEffect | OK — old class still fully functional |

## Post-Merge Missile Fixes Already Applied
These were fixed in earlier sessions (not this audit):
1. Speed oscillation → 20% minimum speed floor
2. Homing missiles orbiting → turn rate boost in Hitting state (up to 3x)
3. Speed freeze → allowPassBy gated by TerrainHeightAware
4. Guided missiles aiming at cruise altitude → aim at target in Hitting state
