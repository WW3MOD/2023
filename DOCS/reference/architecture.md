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
│   ├── maps/                       # 13 maps + test scenarios under maps/test-*/
│   ├── bits/                       # Sprites, sounds, models
│   ├── chrome/                     # UI layouts
│   ├── sequences/                  # Animation definitions
│   └── mod.yaml                    # Mod manifest
├── DOCS/                           # Curated reference docs (this file lives here)
│   ├── ARCHITECTURE.md             # This file
│   ├── BALANCE_REVIEW.md           # Balance reference, linked from DOCS/skills/BALANCE.md
│   ├── PROJECT_ASSESSMENT.md       # Comprehensive project assessment (March 2026)
│   ├── SHADOW_LOS_PLAN.md          # Plan for distance-based shadow falloff (in v1)
│   └── archive/                    # Historical: old design docs, superseded TODOs, etc.
├── DOCS/skills/                         # Workflow triggers — see DOCS/skills/README.md
├── WORKSPACE/                         # Working state (RELEASE_V1, HOTBOARD, BACKLOG, plans, …)
├── tools/                          # Development tools
│   ├── map-mcp/                    # MCP Map Creation Server (TypeScript/Node.js)
│   ├── combat-sim/                 # Tick-by-tick combat simulator (used by DOCS/skills/BALANCE.md)
│   └── test/                       # Developer test harness (used by DOCS/skills/AUTOTEST.md)
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
| CohesionMoveModifier.cs | World trait (IModifyGroupOrder): offsets group move targets based on CohesionMode (Tight/Loose/Spread). Preserves relative formation shape with capped offsets |
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

## Suppression system

**Infantry suppression (10-tier, cap 100, decay 1/5 ticks):**
- `GrantExternalCondition` warheads with Amount/Range for graduated suppression
- Speed/vision/burst/inaccuracy multipliers (90%→0% across tiers)
- InfantryStates triggers prone at suppression > 30
- 10-tier pip display (pip-suppression-1 through pip-suppression-10)

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
