# WW3MOD Architecture

System reference for engine and gameplay code. Linked from `CLAUDE.md`. The agent doesn't need this loaded by default — read when actually working on a system below.

## Project layout

```
WW3MOD/
├── engine/                         # Modified OpenRA release-20230225
│   ├── OpenRA.Game/                # Core engine (Map, Actor, Graphics, Network)
│   ├── OpenRA.Mods.Common/         # Shared traits, activities, widgets (MOST changes here)
│   │   ├── Traits/                 # Unit behaviors, conditions, targeting
│   │   │   ├── Air/                # Aircraft movement (Aircraft.cs, Fly.cs, Land.cs)
│   │   │   └── ...
│   │   ├── Activities/             # Movement, attack, resupply activities
│   │   ├── Warheads/               # Damage, suppression, effects
│   │   ├── Projectiles/            # Bullet.cs, Missile.cs (bypass system)
│   │   └── Widgets/                # UI widgets (MiniMap, CommandBar)
│   ├── OpenRA.Mods.Cnc/            # C&C-specific (some removed/modified)
│   └── OpenRA.Platforms.Default/
├── mods/ww3mod/                    # Mod content (~178MB)
│   ├── rules/
│   │   ├── ingame/                 # Unit definitions (22 YAML files)
│   │   │   ├── aircraft.yaml       # Base aircraft templates (^Aircraft, ^Helicopter, ^Drone)
│   │   │   ├── aircraft-america.yaml / aircraft-russia.yaml
│   │   │   ├── infantry.yaml       # Base infantry templates
│   │   │   ├── infantry-america.yaml / infantry-russia.yaml
│   │   │   ├── vehicles-america.yaml / vehicles-russia.yaml
│   │   │   ├── structures.yaml / structures-defenses.yaml
│   │   │   ├── defaults.yaml       # Global defaults (^ExistsInWorld, ^GainsExperience, etc.)
│   │   │   └── world.yaml          # World actor, factions, palettes
│   │   ├── weapons/                # Weapon definitions (7 files)
│   │   ├── ai/                     # AI configuration (ai.yaml, ai-america.yaml, ai-russia.yaml)
│   │   └── misc.yaml               # Crates, mines, misc actors
│   ├── maps/                       # 10 maps + test scenarios under maps/test-*/
│   ├── bits/                       # Sprites, sounds, models
│   ├── chrome/                     # UI layouts
│   ├── sequences/                  # Animation definitions
│   └── mod.yaml                    # Mod manifest
├── DOCS/                           # Static reference — see DOCS/README.md
│   ├── reference/                  # This file, game-model, supply-route, economy, pitfalls, …
│   ├── modes/                      # Operating modes — see DOCS/modes/README.md
│   ├── recipes/                    # Workflow triggers — see DOCS/recipes/README.md
│   ├── gameplay/                   # Player-perspective mechanic docs — see DOCS/gameplay/README.md
│   └── archive/                    # Historical: old design docs, superseded TODOs, etc.
├── WORKSPACE/                      # Living state (RELEASE_V1, HOTBOARD, BACKLOG, plans, …)
├── tools/                          # Development tools
│   ├── map-mcp/                    # MCP Map Creation Server (TypeScript/Node.js)
│   ├── combat-sim/                 # Tick-by-tick combat simulator (used by DOCS/recipes/BALANCE.md)
│   └── autotest/                   # Developer test harness (used by DOCS/recipes/AUTOTEST.md)
├── .mcp.json                       # MCP server configuration
├── CLAUDE.md                       # Agent instructions
├── WW3MOD.sln                      # Visual Studio solution
├── Makefile / make.ps1             # Build system
└── mod.config                      # Build configuration
```

## Scenario System

Scenarios are scripted map variants that share terrain with a base map but add different units, players, and Lua scripts. They appear in the lobby map chooser under the "Scenario" category.

### How it works

- A scenario is a **separate map folder** that copies `map.bin` (terrain) from a base map
- Has its own `map.yaml` (different actors, players), `rules.yaml` (LuaScript reference), and `.lua` script
- Uses `Categories: Scenario` to appear in the Scenario filter in the map chooser
- No engine C# changes needed — everything runs on OpenRA's existing Lua scripting API
- Supports multiplayer + bots — human players take specific slots, bots fill the rest

### Creating a scenario

1. Create a new map folder: `mods/ww3mod/maps/<base-map>-<scenario-name>/`
2. Copy `map.bin`, `shadows.bin`, `map.png` from the base map
3. Write `map.yaml` with:
   - `Categories: Scenario` and `LockPreview: True`
   - Custom players (human playable + non-playable garrison/AI factions)
   - Pre-placed actors (garrison units, supply routes, objectives)
4. Write `rules.yaml` with `LuaScript: Scripts: scenario.lua, <your-script>.lua`
5. Write your scenario `.lua` script using the `Scenario` helper library

### Scenario Lua library (`mods/ww3mod/scripts/scenario.lua`)

Reusable helpers for scenario scripts:
- **Spawning**: `Scenario.SpawnUnit()`, `Scenario.SpawnGroup()`, `Scenario.ReinforceFromEdge()`
- **Ownership Transfer**: `Scenario.TransferAll(from, to)`, `Scenario.ScheduleTransfer(from, to, delaySec)`
- **Wave Spawning**: `Scenario.ScheduleWave(wave, delaySec)`, `Scenario.ScheduleWaves(waves, base, interval)`
- **Patrol/Defense**: `Scenario.Patrol(actors, waypoints)`, `Scenario.DefendPosition(actors)`
- **Messaging**: `Scenario.Message(text)`, `Scenario.SetBriefing(text)`, `Scenario.PlaySpeech(player, notif)`
- **Objectives**: `Scenario.AddPrimaryObjective(player, desc)`, `Scenario.CompleteObjective(player, id)`
- **Utility**: `Scenario.GetLiving(tag)`, `Scenario.CountLiving(tag)`, `Scenario.OnGroupEliminated(tag, cb)`

### Naming convention

Scenario titles follow the format **`<Scenario>: <Map Name>`** — scenario name first, then the base map. This lets the same scenario type apply across multiple maps (e.g., "Frontline: River Zeta WW3", "Frontline: Siberian Pass WW3"). Feels like a game mode.

### Key Lua APIs used

| API | Purpose |
|---|---|
| `actor.Owner = player` | Transfer unit ownership |
| `Actor.Create(type, true, inits)` | Spawn new actors |
| `Reinforcements.Reinforce(owner, types, path, interval)` | Edge reinforcements |
| `Trigger.AfterDelay(ticks, func)` | Timed events |
| `Trigger.OnAllKilled(actors, func)` | Group elimination triggers |
| `player.AddPrimaryObjective(desc)` | Mission objectives |
| `UserInterface.SetMissionText(text)` | HUD briefing text |
| `Media.DisplayMessage(text, prefix)` | Chat log messages |
| `Media.PlaySpeechNotification(player, notif)` | EVA voice lines |

## Regenerating shadows.bin

Each map keeps a precomputed `shadows.bin` LOS cache. Changes to the shadow compute pipeline (e.g. `CellLayer.IsValidCoordinate`, `RecomputeShadowFrom`, density formulas) invalidate every cached file — the bug stays baked in until the cache is rebuilt. Two ways to refresh:

```bash
./utility.sh --regen-shadows ../mods/ww3mod/maps/<name>   # narrow: only rewrites shadows.bin
./utility.sh --refresh-map ../mods/ww3mod/maps/<name>     # wide: also rewrites map.yaml and map.png
```

Note the `../` — `utility.sh` cd's into `engine/` before running. Saving a map in the in-game editor also triggers a regen. After a shadow-compute fix, refresh every map under `mods/ww3mod/maps/` that has a `shadows.bin` (currently: `river-zeta-ww3`, `woodland-warfare-ww3`).

## Key engine modifications

These are the custom systems that set WW3MOD apart from base OpenRA. Understanding these is critical before modifying any engine code.

### Renamed/rewritten core systems

| Original → Custom | Purpose |
|---|---|
| Shroud.cs → MapLayers.cs | Complete vision/shroud rework with graduated visibility |
| ShroudRenderer → MapLayersRenderer | Rendering for new vision system |
| Crushable.cs → Passable.cs | Richer obstacle interaction (fences, mines, trees) |
| TakeCover.cs → InfantryStates.cs | Infantry behavior model (prone at suppression > 30) |
| AffectsRadar → Radar.cs + Detectable.cs | Multi-layer detection/visibility |
| RadarWidget → MiniMapWidget | Renamed + reworked minimap |

Per-player visibility is queried as `player.MapLayers.IsVisible(cell, 1)` — the `1` is the visibility **threshold** meaning *currently unfogged* (`World.cs:110`: `FogObscures(CPos)` is exactly `!RenderPlayer.MapLayers.IsVisible(p, 1)`). The weaker "ever seen / explored" shroud state is `MapLayers.IsExplored(...)` (`World.cs:112-115`). This is the canonical fog-legal predicate for any per-player layer that must not see through fog.

### Custom traits (new files)

| Trait | Purpose |
|---|---|
| Detectable.cs | Graduated visibility (cloaked/spotted/revealed) with additive modifiers |
| BlocksSight.cs | Objects that block line of sight |
| Radar.cs | Custom radar detection with range/conditions |
| ShockwaveDamageWarhead.cs | Explosive blast wave effects |
| InfantryStates.cs | Infantry states replacing TakeCover |
| EjectOnHusk.cs | Crew ejection from destroyed vehicles |
| GarrisonManager.cs | Shelter/port deployment model with IDamageModifier (indestructible at 1HP), dynamic ownership (enter→claim, empty→neutral), suppression-aware ports (duck at 30+, recall at 60+, lockout), ambush integration |
| GarrisonProtection.cs | Damage pass-through to shelter occupants only (port soldiers have DamageMultiplier via garrisoned-at-port condition) |
| GarrisonPortOccupant.cs | ITargetable on infantry: directional port targetability — soldiers only targetable by enemies within port's firing arc |
| WithGarrisonDecoration.cs | Garrison pips (centered bottom) + protection % text overlay (color-coded) + empty port indicators |
| GarrisonPanelLogic.cs | Sidebar panel for garrison management (shows deployed + shelter soldiers) — pending icon rewrite |
| SupplyProvider.cs | Greatest-need resupply: 1 pip per cycle, cycles to unit with most need, limited supply capacity |
| QuickRearm.cs | Enter-truck instant rearm: infantry enters Cargo, auto-ejected after delay with full ammo |
| HealerClaimLayer.cs | World trait: prevents multiple medics targeting same patient (healer→patient 1:1 claims) |
| HealerAutoTarget.cs | IOverrideAutoTarget: smart healer targeting — HP% scoring, critical priority, stabilize-and-switch |
| VehicleCrew.cs | Vehicle crew slots (Driver/Gunner/Commander), eject on critical damage, re-entry, commander substitution |
| CrewMember.cs | Crew infantry trait: IIssueOrder for re-entering vehicles with matching empty slots |
| EnterAsCrew.cs | Activity: crew walks to vehicle, fills slot, gets removed from world |
| SmartMove.cs | IWrapMove + INotifyDamage: wraps Move orders so units selectively fire while moving (self-defense or unsaturated targets). Overkill check via AverageDamagePercent |
| SupplyRouteContestation.cs | Graduated SR control bar: enemy vs friendly value comparison, depletion/recovery, IProductionSpeedModifier for dynamic production slowdown, visual/audio feedback |
| UnitDefaultsManager.cs | World trait: per-type stance defaults persisted to `Platform.SupportDir/ww3mod/unit-defaults.yaml`. Ctrl+Alt+Click stance buttons sets type default for all future units |
| CohesionMoveModifier.cs | World trait (IModifyGroupOrder): intent-aware cover-placement system for grouped Move/AttackMove orders. Classifies the target cell against `Map.DensityLayer` and dispatches to one of four slot strategies: **Open** (box layout — fires on open terrain, the typical AI case), **SpreadInside** (bid for top-CoverScore cells inside a cover patch), **EdgeLine** (line perpendicular to the density gradient, anchored at the cover edge), **Approach** (boundary-anchored line when the squad is far from a cover click). CohesionMode (Tight/Loose/Spread) controls only the **slot spacing** (col/row WDist), not which strategy fires. See `CohesionMoveModifier.cs:19-26` (class desc), `:126-143` (spacing), `:178-214` (intent classification). Fires for bot-issued grouped orders (confirmed: `UnitOrders.cs:397-413`). AI default is `Loose` (`AutoTarget.cs:120 InitialCohesionAI`). The **Open** box footprint is **bounded per mode**: once `(cols-1)*colSpacing` would exceed `{Tight,Loose,Spread}MaxWidth` (`:54-73`) the effective spacing shrinks to hold the span at the cap (same for depth), floored at `MinSlotSpacing` (1024) — so the box no longer grows without limit as unit count rises. Mode ordering Tight<Loose<Spread is preserved for every count. Regroup-on-arrival is not new code — the bound repurposes the existing `CohesionSlotMemory` sticky-slot leash. |
| SightingThreatLayer.cs | World trait: **per-player, fog-respecting** threat/friendly field over `CellLayer<SightingCell>` (contrast the omniscient `InfluenceMap`/`ThreatMapManager`, which scan `world.Actors` with no fog check, `InfluenceMap.cs:92-102`). Decaying memory: each staggered recompute multiplies by `DecayPercent` and re-injects fresh sightings, so recent contact dominates. Enemy sources are strictly per-player-legal — currently-visible via `Actor.CanBeViewedByPlayer`, fog-frozen last-seen via `FrozenActorLayer.FrozenActorsInRegion(…, onlyVisible:true)`. Surfaces `ThreatIntensity`/`ThreatDirection` (a deterministic integer `WAngle.ArcTan` bearing) + `FriendlyIntensity`. Additive accumulation ⇒ order-independent, sync-safe. NOTHING consumes it for behaviour yet (Phase 1 of the strategic/tactical split). |
| TerrainAffordanceLayer.cs | World trait: **static, player-agnostic** cover map computed once at `WorldLoaded` from `Map.DensityLayer`. Per passable cell: `CoverQuality` (summed 8-neighbour density) + edge classification, where the **outward normal is the negated density gradient** (`(-gradX,-gradY).Yaw` points out toward open ground; interior cells have ~0 gradient). Makes "an edge cell facing the threat direction" a lookup. Identical on every client ⇒ no sync concern. |
| SightingIntelOverlay.cs | RENDER-ONLY `IRenderAnnotations` on the World actor, gated on the existing hold-Space `wr.ShowAllOrders`. Draws a balance-of-power wash (green friendly / red enemy / computed gray) + GPS dots for fog-frozen enemy structures, reading only `world.RenderPlayer ?? world.LocalPlayer`'s own §3a layer — leaks nothing through fog. Dev always-on via `/intel` chat command. (Mobile enemies leave no frozen actor — only structures carry `FrozenUnderFog` — so their last position survives only as decaying `SightingThreatLayer` intensity, not a dot.) |
| PatrolOrderGenerator.cs | Order generator for patrol waypoint queuing mode. Click adds waypoints, click Patrol again to confirm |
| Patrol.cs (Activity) | Loops waypoints with attack-move. Circular if last≈first waypoint, otherwise bounce (A→B→C→B→A→...) |
| HeliEmergencyLanding.cs | Helicopter emergency landing: autorotation on heavy damage (steerable, safe landing, crew evacuates to neutral), uncontrolled crash on critical (spinning, destroyed, everyone dies). Integrates with VehicleCrew for crew ejection and AllowForeignCrew for capture |
| CargoSupply.cs | TRUK-only numeric supply pool. Auto-rearms nearby allied AmmoPool units within RearmRange. Pool drains as ammo is given; never regenerates. `IIssueDeployOrder` drops the entire pool as a SUPPLYCACHE on the truck's cell (merges into existing cache if present). `IIssueOrder` lets the player right-click an LC to queue a delivery move. Empty + Auto stance seeks nearest LC for refill (LC's pool drains 1:1); Hold sits; Evacuate rotates to map edge for credit return |
| CargoPanelLogic.cs | Sidebar panel for transport cargo management: individual eject, mark for waypoint unload, rally points, supply drop |
| CargoUnloadOrderGenerator.cs | Click-on-map order generator for waypoint-based selective unloading of marked passengers |
| EjectRallyOrderGenerator.cs | Click-on-map to set per-passenger post-eject rally point (move target on ejection) |

### Heavily modified systems

| File | Key changes |
|---|---|
| DamageWarhead.cs | Suppression hooks, damage falloff, bypass integration |
| AutoTarget.cs | Value-based targeting priorities |
| Armament.cs | Multi-weapon, reload timing, ammo integration |
| AmmoPool.cs | Extended ammo/resupply mechanics, SupplyValue per-ammo cost |
| Bullet.cs | Projectile bypass through obstacles (BlocksProjectiles) |
| Aircraft.cs | Velocity-based movement for helicopters (see Aircraft section) |
| Fly.cs | Acceleration/deceleration for CanSlide aircraft |
| Missile.cs | FlyStraightIfMiss (missiles fly straight after passing target) |
| PlayerResources.cs | Economy/upkeep modifications |
| Map.cs | Map loading, bounds, layer support |
| AttackGarrisoned.cs | Rewritten: per-port firing via GarrisonManager, legacy fallback preserved |

## Aircraft movement system

The air branch introduced a **dual movement system** that is important to understand:

### Helicopters (CanSlide = true)

Use velocity-based movement with acceleration/deceleration:
- `Aircraft.CurrentVelocity` — current movement vector
- `Aircraft.RequestedAcceleration` — set by Fly activity each tick
- `Aircraft.CalculateAccelerationToWaypoint()` — computes acceleration toward target, includes maintenance accel to prevent speed oscillation
- `Aircraft.CalculateStopPosition()` — predicts stop position using discrete semi-implicit Euler formula
- Movement applied in `Aircraft.Tick()` via `CurrentVelocity` (decel THEN move)
- `Fly.Tick()` has a **fully separate CanSlide code path** — only sets RequestedAcceleration, never calls FlyTick
- Always brakes toward target (stopAtWaypoint=true), even when activities queued after
- Altitude adjustment during flight: gradually climbs/descends toward CruiseAltitude while flying
- On arrival: snaps to exact target position, zeros CurrentVelocity. Skips climb if next is Land
- Landing: smooth speed-proportional descent (fast=high, slow=low), gentle touchdown near ground
- Takeoff: rise to halfway CruiseAltitude, then start moving forward while climbing rest
- Pitch applied during horizontal movement in Aircraft.Tick (FlyTick isn't called for CanSlide)
- **CRITICAL**: Never use FlyTick for CanSlide without zeroing CurrentVelocity first (double movement)

### Fixed-wing (CanSlide = false)

Use traditional step-based movement:
- `Aircraft.FlyStep()` — returns movement vector for current speed/facing
- `Fly.FlyTick()` — applies movement, handles altitude, roll, pitch
- Turns computed via `Fly.CalculateTurnRadius()`

### Key Aircraft YAML properties

```yaml
Aircraft:
    Speed: 100                  # Movement speed
    TurnSpeed: 12               # Turn rate (WAngle units)
    IdleTurnSpeed: 8            # Turn rate when idle
    IdleSpeed: 25               # Speed when idle/patrolling
    MaxAcceleration: 5          # Acceleration per tick (for CanSlide)
    RotationAcceleration: 2     # Turn acceleration (for CanSlide)
    CruiseAltitude: 3c768       # Normal flight altitude
    AltitudeVelocity: 100       # Vertical movement speed
    CanSlide: True/False        # Helicopter vs fixed-wing
    CanHover: True/False        # Can stop mid-air
    VTOL: True/False            # Vertical takeoff/landing
    Repulsable: True/False      # Pushed away from other aircraft
    MaximumPitch: 56            # Max climb/dive angle
```

### Attack standoff (per-target, engine-provided)

The engine already stands a hovering/sliding attack aircraft off at weapon range — helicopter *overflight* past nearer enemies is a bot-order artifact, not an attack-code bug:

- A **Hover** attack aircraft (`AttackType: Hover` + `CanHover`) neither strafes nor takes a fixed-wing attack run in `FlyAttack.Tick`; it falls through to the facing branches (`FlyAttack.cs:183-198`), so it hovers and fires from `MoveWithinRange` distance instead of running past the target.
- A `CanSlide` aircraft's `Fly` activity zeroes `CurrentVelocity` and stops the moment it is inside the weapon annulus (`Fly.cs:187-190`). So a `MoveWithinRange`/`Attack` on a single actor already yields a per-target standoff at that armament's max range.
- **Overflight is a targeting choice, not an engine defect.** A bare `Order("Attack", unit, oneEnemy)` flies to *that* actor's standoff, overflying nearer front-line enemies en route (they are in range but are not the locked target). `Order("AttackMove", …)` instead lets `AutoTarget` engage the nearest in-range threat and only advance when clear — the standard way to get "stop and shoot what's in front of me" behaviour from a squad or unit.

## Suppression system

**Infantry suppression (10-tier, cap 100, decay 1/5 ticks):**
- `GrantExternalCondition` warheads with Amount/Range for graduated suppression
- Speed/vision/burst/inaccuracy multipliers (90%→0% across tiers)
- InfantryStates triggers prone at suppression > 30. **Prone is damage/visual/speed only — it grants NO detection or concealment reduction** (there is no prone-stealth modifier; visibility is purely whether the actor occupies a cell the enemy's `MapLayers` reveals, `Detectable.cs:93-116`). To stay hidden, halt before entering enemy vision — posture does not help.
- 10-tier pip display (pip-suppression-1 through pip-suppression-10)
- **Suppression is not a blanket fire-halt** — suppressed infantry keep firing, just degraded by the multipliers above. The exceptions are three armaments that hard-pause via `PauseOnCondition: suppressed >= 10`: the AT Specialist ATGM (`infantry.yaml:1652`), the engineer mine-clear/repair arm (`:1865`), and the medic heal arm (`:2136`). So a "let it pass, then shoot the rear" AT ambush that lets the escort return fire risks suppressing its own AT gunner into silence before it exploits the rear arc.

**Vehicle suppression (5-tier, cap 50, decay 1/3 ticks):**
- Only medium caliber (12.7mm+) and explosions suppress vehicles
- Turret turn speed reduced (85%→25%), inaccuracy increased (115%→200%)
- Burst wait increased (105%→150%), NO speed reduction
- Defined in `^VehicleSuppressionEffects` template in vehicles.yaml

**Fire discipline (3 stances — controls WHEN to fire):**
- HoldFire, Ambush, FireAtWill (default)
- Ambush: pre-aim at targets, hold fire until spotted or damaged, coordinate with nearby allies
- FireAtWill: fire at any valid target in range
- Conditions: `stance-fireatwill`, `stance-ambush`, `stance-holdfire`

## Directional / rear armor

`DamageWarhead.ArmorDirectionPercent` (`DamageWarhead.cs:121-198`) scales effective armor by the shot-vs-facing angle, reading a 5-element `Armor.Distribution` `[front, side, rear, top, bottom]` (only applied when `Distribution.Length == 5`). A heavy tank's `100,50,25,10,10` means a rear shot lands ~4× the front damage. This runs inside the normal damage pipeline, so rear/flank bonuses are **automatic** from geometry — no special warhead flag or code path is needed; putting the shooter behind the target is the whole trigger.

**Engagement stances (3 stances — controls WHERE to position):**
- HoldPosition, Defensive (default), Hunt
- Separate from fire stances — two independent UI bars (3 buttons each)
- Hunt: chase targets aggressively, even out of range
- Defensive: fire from current position, reposition only if LOS blocked (Phase 2: cover-seeking via ShadowLayer — see `DOCS/reference/shadow-los-plan.md`)
- HoldPosition: never auto-reposition, only fire from current cell
- Hotkeys: Alt+A/G/F (fire), Ctrl+Alt+A/D/F (engagement)
- Engagement stance drives `allowMove` in AutoTarget scanning and movement decisions in Attack activity

**Cohesion stances (3 stances — controls HOW close together, Phase 1 UI only):**
- Tight, Loose (default), Spread
- Hotkeys: Ctrl+Alt+1/2/3
- Phase 3 will modify waypoint distribution on group moves (not repulsion — too laggy for ground units)

**Resupply behavior (3 stances — controls WHAT to do when out of ammo):**
- Hold (stay put, flag NeedsResupply for supply truck pickup), Auto (seek nearest supply point, default), Evacuate (leave via Supply Route)
- Only shown for units with AmmoPool trait
- Hotkeys: Ctrl+Alt+4/5/6
- AutoRearmIfAllEmpty checks stance: Auto=seek, Hold=flag+wait, Evacuate=RotateToEdge
- Supply trucks in Hunt stance actively seek NeedsResupply-flagged units map-wide

**Click-modifier meta-system (all 4 stance bars):**
- Click: Set stance for current selection (immediate)
- Ctrl+Click: Set per-unit default (unit remembers even after resets)
- Ctrl+Alt+Click: Set per-type default — all future units of this type spawn with this. Persisted to disk via UnitDefaultsManager
- Alt+Click: "Do Now" order — Fire/Engagement: set stance + cancel all orders. Resupply: immediate action (go resupply/stop/evacuate). Cohesion: set stance + reposition group

## AI configuration

AI is configured entirely via YAML in `mods/ww3mod/rules/ai/`:
- `ai.yaml` — ModularBot setup, shared modules (BuildingRepairBotModule, CaptureManagerBotModule, SquadManagerBotModule for air, HelicopterSquadBotModule)
- `ai-america.yaml` — America-specific build priorities, unit limits, squad composition
- `ai-russia.yaml` — Russia-specific same

Key AI modules: `UnitBuilderBotModule` (what to build), `SquadManagerBotModule` (how to attack), `HelicopterSquadBotModule` (helicopter attack/scout/transport squads), `CaptureManagerBotModule` (what to capture), `BuildingRepairBotModule` (auto-repair).

**Important for aircraft modules:** Helicopter `UnitBuilderBotModule` uses `SkipRearmBuildingCheck: true` because helicopters are called in via Supply Route and don't need an HPAD to be produced. Without this flag, the old RA check (`HasAdequateAirUnitReloadBuildings`) blocks aircraft production when no rearm building exists.

**Two more full-ammo gates brick attack-heli *squads* (separate from production).** WW3MOD attack helis rearm only at an HPAD and the mod builds none, so any heli that fired dips below full and never refills. That trips two independent launch gates: (1) `HelicopterSquadBotModule.IsReadyForMission` requires every `AmmoPool` at full for a heli with `AmmoPool`+`Rearmable` (`:408`) — no squad ever *forms*; (2) the squad FSM's `SquadHasAmmo` (`Squads/States/HelicopterStates.cs:118-131`) *skips* every unit whose pools are all covered by a `Rearmable` (`ReloadsAutomatically` true — exactly attack helis) then returns false if none remain, so an all-attack-heli squad reports "no ammo" **even at full** and the idle/withdraw/re-engage gates never pass — the squad *forms but never launches*. Both are bypassed by `SkipRearmReadyCheck` (default-off, experimental only); the production `SkipRearmBuildingCheck` does **not** cover either. Corner-idle helis are arrival logic (`ProductionFromMapEdge` flies them to the SR/edge cell with no rally Path), not RA idle-return.

**Out-of-ammo evac is a unit-level `AmmoPool` behaviour, invisible to bot modules.** `AmmoPool.AutoRearmIfAllEmpty` `case Evacuate` queues `RotateToEdge` (`AmmoPool.cs:197-204`); WW3MOD vehicles opt in via `InitialResupplyBehaviorAI: Evacuate`. No bot module reads the resulting state, and the evac path never commits the unit to the `PoiGoalGuard` ledger — so an evacuating unit is "free" to any module that lacks an ammo filter and can be recruited back onto an axis, overwriting its retreat. `LayeredDefenceBotModule` is the only module that guards it (`SkipOutOfAmmoUnits`, default true, `:102,277`; `IsOutOfAmmo` = all pools at 0, `:469`). Any module pulling units by proximity/idle needs this guard or a shared evac reservation.

### AI production: `UnitsToBuild` weights are share *ceilings*, not priorities

`UnitBuilderBotModule` (the shared "what to build" module) has **no YAML field for a production floor or priority** — all three of its Info dictionaries are caps or gates, never guarantees:

- **`UnitsToBuild`** — the weight is a per-type *share ceiling as a percent*, not a priority. `ChooseUnitToBuild` (`UnitBuilderBotModule.cs:177-195`) **shuffles** the dict and returns the first entry passing `count*100 < weight*total` (`:190`), i.e. `count/total < weight/100`. Any weight ≥ 100 can never bind, so that unit is merely "always eligible" and is then picked **uniformly** by the shuffle — weight `500` and weight `120` give identical early-game odds. Below the roster-average weight a type is *throttled*; above it there is no boost.
- **`UnitLimits`** is a ceiling; **`UnitDelays`** is a start delay.
- While `idleBaseUnits < IdleBaseUnitsMaximum` (12, `:25`) the module ignores weights entirely and builds a **uniform-random** buildable (`ChooseRandomUnitToBuild :167-175`), discarding picks not in `UnitsToBuild` (`:125-126`).

A guaranteed "keep N of type X ready" therefore requires **code**, via the `IBotRequestUnitProduction` demand queue (the same queue `AdaptiveProductionBotModule` uses, `:64,159`):

- Call `up.RequestUnitProduction(bot, name)` on each `player.PlayerActor.TraitsImplementing<IBotRequestUnitProduction>()` (in WW3MOD only `UnitBuilderBotModule` implements the sink). `BotTick` pops **one** queued request per `FeedbackTime` (30-tick) cycle **before** the share lottery (`:87-92`) and routes it through the single-name `BuildUnit` overload (`:142-165`), which skips `UnitsToBuild`, `UnitLimits`, and `UnitDelays`.
- **Drop-on-failure is real** (`:90-91` removes the entry whether or not a queue was free), so a floor must **re-request every scan** and subtract already-queued via `RequestedProductionCount` (`:104-107`) — gate on `alive + pending < floor`, checked every scan.
- For a faction-correct build type with no hardcoding, intersect the desired types with the player's queue `BuildableItems()` names, and resolve lazily without caching a null (queues/prereqs may be cold on the first scan).

`CaptureCoordinatorBotModule.MaintainTecnFloor` (`:389-402`, default-off `TecnFloor`) is the reference implementation.

### Adding a behavioural field to a trait shared by both bot profiles

`ModularBot@experimental` and `ModularBot@stable` (`ai.yaml:41-46`) share the same trait classes; `@stable` is the **frozen validated snapshot** used as a benchmark control. A new Info field with a non-baseline **code default** (e.g. `PoiOffensiveBotModule.ApproachCohesion = Spread`, `:96`) therefore leaks into `@stable` even when its YAML is untouched — silently mutating the control. Rule: **any behavioural Info field added to a shared trait must default to the frozen/baseline behaviour and be opted in per-profile via YAML.** The dispersion work does this with `CohesionSwitchEnabled` (default `false`, `:87`; the dispersion path is gated on it at `:424`), flipped `true` only on `@experimental`.

### Bot decisions ARE seed-reproducible (since main @ 2d3c8fe0)

`World.cs:213` seeds `SharedRandom` from the lobby `RandomSeed` (deterministic, network-synced); `World.cs:214` now also seeds `LocalRandom` — from that same `RandomSeed` via a fixed decorrelating transform `(int)(RandomSeed*6364136223846793005 + 1442695040888963407)`, guarded on `RandomSeed != 0` so normal gameplay (seed = `DateTime.Now`) still varies per launch. The bot modules make their *decisions* off `world.LocalRandom` (e.g. `UnitBuilderBotModule` picking which unit to call in; squad / layered-defence / support-power scan timing and target choice), so before this fix `LocalRandom` was unseeded (`new MersenneTwister()` → `Environment.TickCount`) and two same-`Test.RandomSeed` runs diverged within ~125 ticks. **Now a fixed seed is a *reproduction*:** verified byte-identical verdicts (and tick-by-tick score logs) across two seed-1017 hidden Mode-B matches, with a different-seed negative control diverging as expected (`WORKSPACE/ai-bench/runs/260720_seeded_determinism_verify.md`). The derived seed is decorrelated from `SharedRandom`'s combat rolls so the two MT streams stay independent. The verdict JSON records the seed (`verdict_version` 5). Note: OpenRA's off-thread (async) pathfinding did **not** need any extra work for this — it applies its results deterministically on the sim thread even with WW3MOD's modified movement, so seeding the single unseeded `LocalRandom` was sufficient for full byte-identical replay. Aggregate-over-N benchmarking is still the right way to *evaluate a code change* (one seed is one battlefield); what seeding buys is a *stable* mean over a fixed seed-set. Note: `LocalRandom` is "local" (non-synced) by OpenRA design — this makes a single-client benchmark reproducible, not a multiplayer match synced.

### `RenderPlayer` is render-side only

`world.RenderPlayer` never touches the sim or the sync hash. `FogObscures`/`ShroudObscures` all short-circuit to `false` when `RenderPlayer == null` (`World.cs:109-114`), no player's `MapLayers` is mutated, and the sync hash reads `p.UnlockedRenderPlayer`, not `world.RenderPlayer` (`World.cs:543-547`). So switching a client to world-view (null RenderPlayer) leaves AI perception and the test verdict byte-identical — unlike the `DeveloperMode` "disable shroud" cheat, which does a **synced** `MapLayers.ExploreAll()` + `FogDisabled = true` per-player and thus changes targeting and the sync hash. Two consequences for tooling:

- **A World-actor render overlay must fall back to `LocalPlayer`** — in the autotest harness `world.RenderPlayer` is **null** even though the World-actor's `RenderAnnotations` is still called, so `var viewer = world.RenderPlayer ?? world.LocalPlayer;` is the correct local-client identity (still per-player-legal — reads only the viewer's own layer).
- `ShroudRenderer.UpdateShroud` only clears drawn shroud sprites when a render player is active; flipping `RenderPlayer` to null on a *live* client (world-view / `DevCinematicView`) must still clear each dirty cell's sprites or the map stays black despite uniform visibility.

## Widget / chrome authoring gotchas

Engine widget behaviors that fail **silently** — each cost real debugging time in the lobby work:

- **`ImageWidget` draws sprites at native size.** `Width`/`Height` are layout-only; `Draw()` calls `WidgetUtils.DrawSprite(sprite, RenderOrigin)` and ignores widget bounds (`ImageWidget.cs:78-91`). To scale a sprite into its bounds use the opt-in `ScaleToBounds: True` (uniform, centered). **When you add a field to a widget, mirror it in the copy-constructor** (`ImageWidget.cs:61`) — template clones run through the copy-ctor and silently drop any field you forgot.
- **`ButtonWidget` renders nothing for a missing chrome variant.** A highlighted button looks up `<Background>-highlighted` (`ButtonWidget.cs:320`), plus `-hover`/`-pressed`/`-disabled` suffixes; if that collection is absent, `WidgetUtils.DrawPanel` early-returns with no error and the button draws with no fill. Any custom `Background:` needs the full variant set.
- **Hidden widgets keep keyboard focus.** `Widget.IsVisible` is `() => Visible` (`Widget.cs:231`) — it checks the widget's OWN `Visible` flag, not its ancestors'. A focused `TextField` whose parent tab is hidden still looks visible to the focus system, so it keeps eating key presses (and Enter can fire its `onSelect`). Any tab-switch that hides a focused widget must hand focus off explicitly.
