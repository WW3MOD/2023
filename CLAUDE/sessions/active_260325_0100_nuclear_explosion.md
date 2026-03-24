# Nuclear Explosion Rewrite Session
**Date:** 2026-03-25
**Status:** in-progress

## Task
Rewrite MSLO nuclear explosion with realistic physics-based multi-phase detonation.

## Files Modified
- `engine/OpenRA.Mods.Common/Warheads/ShockwaveDamageWarhead.cs` — Full rewrite: extends DamageWarhead, spawns ShockwaveEffect on DoImpact
- `engine/OpenRA.Mods.Common/Effects/ShockwaveEffect.cs` — **New file**: IEffect + IEffectAnnotation, expanding wavefront with damage + visual ring
- `mods/ww3mod/rules/weapons/weapons-superweapons.yaml` — Atomic weapon rewritten: 53 blast warheads → 1 ShockwaveDamage, added thermal/EMP/fire phases

## Key Changes
- Thermal radiation (instant): SpreadDamage + graduated onfire (x10 to x1)
- EMP (instant): empdisable on vehicles, 15 cell range
- Blast wave (speed of sound): ~7 ticks/cell, 25 cell radius, ~7 seconds total
- Visual shockwave ring via RangeCircleAnnotationRenderable
- Suppression waves staggered with blast arrival times

## Needs Testing
- Close game, rebuild, test MSLO fire
- Verify phase timing (thermal instant, blast wave slow)
- Tune damage values
