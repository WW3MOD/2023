# WW3MOD Project Assessment

**Date:** March 2026
**Engine Base:** OpenRA release-20230225
**Latest OpenRA Release:** release-20250330

---

## 1. Project Overview

WW3MOD is a total conversion mod for OpenRA that transforms the classic Red Alert RTS into a
contemporary World War 3 scenario. Unlike most OpenRA mods that only customize YAML rules, this
project rewrites significant portions of the engine itself -- 264 C# files modified across 234
commits with ~6,300 insertions and ~3,300 deletions from the base OpenRA release-20230225.

The mod features two primary factions (NATO/America vs BRICS/Russia), realistic modern military
equipment, and deep gameplay mechanics including ammo management, supply routes, experience
systems, and a cover/protection system.

**Authors:** FreadyFish & CmdrBambi
**Repository:** https://github.com/WW3MOD/2023.git
**Total Commits:** 756 (666 on main)

---

## 2. Current State -- What Works

The mod is playable. Core systems function:

- **Two complete factions** with ~15 infantry types each, vehicles, aircraft, and structures
- **Supply Route system** -- units are "called in" from map edges rather than built from factories
- **Ammo management** -- units have limited ammunition and must resupply at Logistics Centers
- **Experience system** -- units gain XP from dealing damage and healing friendlies
- **Cover/protection system** -- terrain provides protection bonuses to infantry
- **Modified pathfinding** -- Crushable renamed to Passable, vehicles interact with obstacles
- **Bypass system** -- projectiles can be blocked by trees and structures (partial)
- **Custom shroud/vision** -- Shroud renamed to MapLayers, custom radar/detection system
- **13 maps** across snow, temperate, and urban terrain
- **Modified auto-targeting** -- value-based target prioritization
- **Custom warheads** -- ShockwaveDamageWarhead, enhanced DamageWarhead with suppression hooks
- **Detectable trait** -- visibility system with additive modifiers for stealth/detection

---

## 3. Key Engine Modifications (C#)

These are the most critical custom engine changes, ranked by scope and importance:

### Core Systems Rewritten
| File | Lines Changed | What Was Done |
|------|--------------|---------------|
| Shroud.cs -> MapLayers.cs | 310 | Complete vision/shroud system rework |
| ShroudRenderer.cs -> MapLayersRenderer | 216 | Rendering for new vision system |
| Map.cs | 177 | Map loading, bounds, layer support |
| Crushable.cs -> Passable.cs | 254 | Obstacle interaction reworked |
| DamageWarhead.cs | 156 | Suppression, damage falloff, bypass |
| AutoTarget.cs | 86 | Value-based targeting priorities |
| Armament.cs | 134 | Multi-weapon, reload, ammo integration |
| AmmoPool.cs | 141 | Extended ammo/resupply mechanics |
| Bullet.cs | 82 | Projectile bypass through obstacles |
| BlocksProjectiles.cs | 76 | Trees/structures blocking shots |
| PlayerResources.cs | 102 | Economy/upkeep modifications |

### New Traits Added
| Trait | Purpose |
|-------|---------|
| Detectable.cs | Graduated visibility system (cloaked/spotted/revealed) |
| Passable.cs | Replacement for Crushable with richer obstacle interaction |
| BlocksSight.cs | Objects that block line of sight |
| Radar.cs | Custom radar detection trait |
| InfantryStates.cs | Infantry behavior states (replaces TakeCover) |
| ShockwaveDamageWarhead.cs | Explosive blast wave effects |
| LeavesTrailsCA.cs | Vehicle trail effects |
| EjectOnHusk.cs | Crew ejection from destroyed vehicles |
| WithWeaponOverlay.cs | Weapon-specific visual overlays |
| WithAddedAnimation.cs | Additional animation layers |
| MapLayersPalettes.cs | Palette support for new vision system |
| GrantConditionOnPreparingAttack.cs | Pre-fire condition system (reworked) |

### Removed/Replaced
| Original | Replacement | Reason |
|----------|------------|--------|
| TakeCover.cs | InfantryStates.cs | Richer infantry behavior model |
| AffectsRadar.cs | Radar.cs + Detectable.cs | Multi-layer detection |
| Tree.cs (Cnc) | Removed | Integrated into Passable/BlocksProjectiles |
| Mine.cs (Cnc) | Removed/reworked | Integrated into Passable system |
| ReloadAmmoPoolCA.cs | Removed | Consolidated into AmmoPool.cs |
| RadarWidget.cs | MiniMapWidget.cs | Renamed + reworked |

---

## 4. What Needs Work -- Priority Assessment

### P0: Critical Bugs (Must Fix)
1. **Nuclear Winter crash** -- NullReferenceException in `EnterAlliedActorTargeter.CanTargetFrozenActor` when many units selected after using superweapon
2. **River Zeta crash** -- `InvalidOperationException` looking for `IResourceLayer` trait on a map with `SeedsResource` actors but no resource layer defined
3. **TECN infiltrates cargo** instead of entering neutral structures
4. **Aircraft spawn blocked** -- units unable to spawn if another unit occupies the waypoint

### P1: Core Gameplay Gaps
1. **Suppression system** -- Hooks exist in DamageWarhead but the actual suppression mechanic is incomplete. This is the single biggest missing feature for the mod's "realism" identity. Currently only partially implemented via conditions.
2. **Vehicle reverse movement** -- Tanks always turn to face movement direction; no reverse gear. Multiple TODO entries reference this.
3. **Ammo economy** -- Ammo is free. The TODO calls for ammo to cost money and require supply truck logistics. This would add strategic depth.
4. **AI is weak** -- AI doesn't build Logistics Centers, doesn't rearm, builds units somewhat randomly. The TODO describes a sophisticated Strategic/Tactical/Operational AI module system but none of it exists yet.
5. **Stances system** -- Extensive design exists in _TODO.txt for multi-toggle stances (fire modes, cohesion, formation, ambush) but only basic Hold/Return Fire/Attack Anything are implemented.
6. **Bleedout animation** -- Death sequence doesn't show; units just disappear.

### P2: Important but Not Blocking
1. **Supply Route improvements** -- Units spawn at the SR location rather than map edge; captured SRs need better handling; each SR should have its own queue
2. **Formation movement** -- No formation support; units blob together
3. **Bypass refinement** -- ATGMs hitting trees too often; tanks firing through forests
4. **Engineer repairs** -- Limited functionality, bridge repair delay missing
5. **Vehicles disabled at 50% HP** -- No recovery vehicle to tow them
6. **Helicopter landing** -- Overshoots and circles when landing; needs deceleration tuning
7. **Map editor** -- Needs more civilian structures, road tiles

### P3: Nice to Have / Future Content
1. Ukraine as third faction (partially started)
2. Nuclear armageddon end-game mode
3. Control group hierarchy with leader units
4. Fire-on-the-move for infantry
5. Kamikaze drones
6. Naval unit scaling and cruise missiles

---

## 5. Engine Upgrade Assessment: release-20230225 -> release-20250330

### The Situation

The mod is based on OpenRA `release-20230225`. The latest OpenRA release is `release-20250330`
(March 30, 2025). That is a ~2 year gap in engine development.

**Your mod modifies 264 C# files in the engine.** This is the crux of the problem. A standard
OpenRA mod that only uses YAML rules could upgrade almost trivially using the `--upgrade-mod`
utility. Your mod rewrites core engine systems.

### What the New Engine Brings
- Revamped Map Editor (significant for your 13 maps)
- Performance improvements (faster load times, rendering)
- HD art asset support improvements
- Bug fixes accumulated over 2 years
- Potentially new traits/features useful for your mod
- Continued community support and multiplayer compatibility

### The Upgrade Approach

There is no automated path for this. The approach would be:

**Phase 1: Catalog Custom Changes (1-2 sessions)**
1. Generate a complete diff of every engine file modified from the base release-20230225
2. Categorize each change as: (a) new trait, (b) modified existing trait, (c) renamed/moved, (d) utility/infrastructure change
3. Document the intent of each change so we know what must be preserved

**Phase 2: Fresh Engine Base (1 session)**
1. Download clean release-20250330 engine
2. Set up a new branch for the upgrade
3. Compare the two engine releases to understand what OpenRA changed

**Phase 3: Reapply Changes -- New Traits (2-4 sessions)**
New traits (Detectable, Passable, BlocksSight, ShockwaveDamageWarhead, etc.) are the easiest --
they are mostly additive. Copy them into the new engine and fix any API changes.

**Phase 4: Reapply Changes -- Modified Core Traits (4-8 sessions)**
This is the hardest part. Files like MapLayers (Shroud), DamageWarhead, Bullet, AutoTarget,
Armament, AmmoPool, and Map.cs have deep modifications interleaved with original OpenRA code.
For each file:
1. Diff the old base vs your modified version to extract your changes
2. Diff the old base vs new base to see what OpenRA changed
3. Manually merge, resolving conflicts

The most painful files will be:
- **MapLayers/Shroud** -- Core vision system, likely changed by OpenRA too
- **Map.cs** -- Almost certainly changed significantly
- **DamageWarhead** -- Combat system, possibly refactored
- **Bullet.cs / Projectiles** -- Likely updated
- **AutoTarget** -- Frequently updated upstream

**Phase 5: YAML Rules Migration (1-2 sessions)**
Run `--upgrade-mod` for automated YAML changes, then manually fix whatever breaks.

**Phase 6: Testing & Fixing (3-5 sessions)**
Compile, run, test each faction, each unit type, each map. Fix runtime issues.

### Estimated Effort

| Phase | Effort | Risk |
|-------|--------|------|
| 1. Catalog changes | 1-2 sessions | Low |
| 2. Fresh engine base | 1 session | Low |
| 3. New traits (additive) | 2-4 sessions | Medium |
| 4. Modified core traits (merge) | 4-8 sessions | **High** |
| 5. YAML migration | 1-2 sessions | Medium |
| 6. Testing & fixing | 3-5 sessions | **High** |
| **Total** | **12-22 working sessions** | |

A "session" here means a focused working period where we're actively collaborating. The high-risk
phases (4 and 6) are where most time will be spent because merge conflicts in core systems
require understanding both what OpenRA changed and what your mod needs.

### My Honest Assessment

**This is doable but significant.** The 264 modified C# files across core engine systems means
every merge will require careful attention. It's not the kind of task where you can run a script
and hope for the best.

**The biggest risk** is the Shroud->MapLayers rework. If OpenRA also significantly changed their
shroud/vision system between releases (which is plausible given the HD asset work), that single
subsystem could take several sessions to merge correctly.

**Recommendation:** Before committing to the upgrade, I suggest we first catalog your engine
changes (Phase 1) and compare against what OpenRA changed. That will give us a much more precise
estimate. If the overlap is small (your changes are in areas OpenRA didn't touch much), the
upgrade will be easier than estimated. If there's heavy overlap, we may want to consider
alternatives.

### Alternatives to Full Upgrade

1. **Stay on release-20230225** -- The mod works. Focus on gameplay features instead. The engine
   version only matters for multiplayer compatibility with other OpenRA players (which doesn't
   apply since this is a total conversion) and for future maintainability.

2. **Selective backport** -- Instead of upgrading your engine, cherry-pick specific improvements
   from the new OpenRA release (e.g., map editor improvements, performance fixes) into your
   existing engine. Less comprehensive but much lower risk.

3. **Extract mod logic into a separate assembly** -- Move your custom traits into
   `OpenRA.Mods.WW3MOD.dll` (which is already referenced in mod.config but doesn't exist as a
   separate project). This would decouple your changes from the engine, making future upgrades
   dramatically easier. This is the right long-term architecture but requires significant
   refactoring now.

**My recommendation:** Option 3 as a long-term goal, but for now, do Phase 1 of the upgrade
assessment to understand the true scope, then decide. If you're happy with the current engine
and just want to finish the mod, staying on release-20230225 is completely valid.

---

## 6. Project Architecture

```
WW3MOD/
├── engine/                    # Modified OpenRA release-20230225 (in-repo, not submodule)
│   ├── OpenRA.Game/           # Core engine (Map, Actor, Graphics, Network)
│   ├── OpenRA.Mods.Common/    # Shared traits, activities, widgets
│   ├── OpenRA.Mods.Cnc/       # C&C-specific traits (some removed/modified)
│   ├── OpenRA.Platforms.Default/
│   ├── OpenRA.Server/
│   └── OpenRA.Utility/
├── mods/ww3mod/               # Mod content (178MB)
│   ├── rules/
│   │   ├── ingame/            # Unit definitions (22 YAML files)
│   │   │   ├── infantry-america.yaml / infantry-russia.yaml
│   │   │   ├── vehicles-america.yaml / vehicles-russia.yaml
│   │   │   ├── aircraft-america.yaml / aircraft-russia.yaml
│   │   │   ├── structures.yaml / structures-defenses.yaml
│   │   │   └── defaults.yaml / world.yaml
│   │   └── weapons/           # Weapon definitions (7 files)
│   │       ├── weapons-ballistics.yaml
│   │       ├── weapons-missiles.yaml
│   │       ├── weapons-explosions.yaml
│   │       └── weapons-superweapons.yaml
│   ├── maps/                  # 13 maps
│   ├── bits/                  # Sprites, sounds, models
│   ├── chrome/                # UI layouts
│   ├── sequences/             # Animation definitions
│   ├── tilesets/              # Terrain definitions (4)
│   └── mod.yaml               # Mod manifest
├── _TODO.txt                  # Massive feature backlog (~570 lines)
├── _BUGS.txt                  # Known bugs with stack traces
├── _RELEASE.txt               # Release notes and plans
├── DOCS/                      # Documentation (this folder)
├── WW3MOD.sln                 # Visual Studio solution
├── Makefile / make.ps1        # Build system
└── mod.config                 # Build configuration
```

---

## 7. Feature Branches (Reviewed March 2026)

**CRITICAL: The `dev` branch has 68 unmerged commits (1164 files, +17711/-3595 lines).** This is
effectively the next version of the mod and should be the first thing addressed.

### Priority Merge

| Branch | Commits | Status | Action |
|--------|---------|--------|--------|
| `dev` | 68 | Major work: doubled HP, critical at 50%, shroud smoothing, River Zeta, cleanup | **Merge first** |
| `air` | 9 (on dev) | Aircraft turn speed, helicopter behavior, missile fixes | Merge after dev |
| `modifiers` | 3 (on dev) | Attack-move modifier key support | Merge after dev |

### Cherry-pick Selectively

| Branch | Commits | Status | Action |
|--------|---------|--------|--------|
| `skane` | 1 (on dev) | Mixed: shroud, GLA music, sprites, River Zeta | Cherry-pick useful parts |
| `xavi` | 2 (on dev) | River Zeta finalization | Cherry-pick map data |
| `maps` | 3 (old base) | Raw map dumps, some duplicates | Extract useful maps |

### Stale / Delete

| Branch | Commits | Status | Action |
|--------|---------|--------|--------|
| `mapwork` | 0 | Fully incorporated into main | Delete |
| `bypass` | 2 (on dev) | Author says "not much progress" | Stale |
| `counterbattery` | 1 (on dev) | Author says "no results in game" | Stale |
| `speed` | 1 (old base) | Single trivial commit | Stale |

**Important:** Most branches (air, bypass, counterbattery, modifiers, skane, xavi) are built
on top of `dev`. Merging dev first will make rebasing the others much simpler.

---

## 8. Recommended Next Steps

If the goal is to **finish the mod** (make it polished and release-ready):

### Step 0: Decide on the `dev` branch (FIRST PRIORITY)
The `dev` branch has 68 unmerged commits with significant changes (doubled HP, critical damage
at 50%, shroud smoothing, cleanup). **You need to decide whether to merge dev into main or
continue from main.** If dev represents your intended direction, merge it first -- everything
else should build on that foundation.

### Immediate (stabilize what exists)
1. ~~Fix the 4 P0 bugs (crashes)~~ **DONE** (March 2026 session)
   - Nuclear Winter crash: null check in EnterAlliedActorTargeter
   - River Zeta crash: disabled SeedsResource on mine actors
   - TECN infiltration: removed CapturesNeutralBuildings from TECN
2. Complete the suppression system (already ~80% done -- see assessment)
3. ~~Improve AI build priorities and enable aircraft~~ **DONE** (March 2026 session)

### Short-term (gameplay depth)
4. Implement basic stances (at minimum: Hold Fire, Ambush, Return Fire, Fire at Will)
5. Vehicle reverse movement
6. Fix bleedout/death animations (needs per-unit rot sprites)
7. Refine bypass system (ATGM vs trees, range-based probability)

### Medium-term (polish)
8. Supply Route improvements (edge spawning, per-SR queues)
9. More maps (the mod needs 6-8 solid competitive maps)
10. Sound and voice work (firing sounds, unit responses)
11. UI polish (widget positioning, support power display)

### Long-term (expansion)
12. Ukraine faction
13. Ammo economy (costs money, supply trucks)
14. Formation system
15. Engine upgrade (if desired)

---

## 9. Technical Debt

1. **Engine modifications are in-repo, not in a separate assembly** -- This is the #1 technical
   debt item. Every engine change is a direct edit to OpenRA source files, making upgrades
   extremely difficult. The mod.config references `OpenRA.Mods.WW3MOD.dll` but it doesn't
   exist as a separate project.

2. **Duplicate TODO sections** -- _TODO.txt has the Supply Route section duplicated verbatim.
   The file mixes bugs, ideas, other mod references, and actionable tasks without clear priority.

3. **Commented-out code** -- Several units (Spy, Dog) are commented out rather than cleanly
   removed. Some engine changes have TODO comments referencing incomplete work.

4. **No automated tests** -- YAML validation exists via `make test` but no gameplay/integration
   tests for the custom engine changes.

5. **Feature branches may have stale work** -- 10+ remote branches that may or may not have
   useful unmerged changes.

---

*This document should be updated as the project progresses. It represents the state of the
project as understood in March 2026.*
