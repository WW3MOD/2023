# WW3MOD - Agent Instructions

## Identity

WW3MOD is a **total conversion** of OpenRA Red Alert into a modern World War 3 RTS. It is NOT a simple YAML mod — it rewrites 264 C# engine files (~6300 insertions, ~3300 deletions) on top of OpenRA `release-20230225`. The engine lives in-repo (not submodule), with `AUTOMATIC_ENGINE_MANAGEMENT="False"`.

**Authors:** FreadyFish (primary dev) & CmdrBambi
**Repository:** https://github.com/WW3MOD/2023.git
**Factions:** NATO/America vs BRICS/Russia (Ukraine planned as third)

## How WW3MOD Differs from Red Alert (CRITICAL — read before any work)

WW3MOD is NOT Red Alert with new sprites. The entire gameplay model is different. **Do not assume any Red Alert mechanic still applies.** Key differences:

### Reinforcement Model (no factories)
There are **no Construction Yards, Barracks, War Factories, or Naval Yards**. Units are NOT "built" — they are **called in as reinforcements from off-map reserves** via the **Supply Route** building. Think of the Supply Route as a radio/logistics hub that requests reinforcements, not a factory that manufactures units.

- **Supply Route** is the single core production building. It produces ALL unit types (infantry, vehicles, aircraft) via `ProductionFromMapEdge` — units spawn at the map edge and march/fly to the rally point.
- **Buildings and defenses** are the exception — they spawn locally at the Supply Route via a separate `Production@Local` queue.
- **HPAD (Helipad)** and **AFLD (Airfield)** are **rearm/repair support buildings**, not production prerequisites. Helicopters and planes CAN be produced without them. HPADs/AFLDs let aircraft rearm faster on-map instead of flying back to the map edge. Future plans include capturable HPADs on maps.
- **"Buying" a unit** = calling in a reinforcement from reserves. **"Rotating out" a unit** = sending it back to the map edge to recover its budget cost. This is the economy loop.
- **Unit costs represent budget allocation**, not manufacturing cost. A destroyed unit is a permanent loss of that budget.

### No tech tree / building prerequisites
There is no "build barracks → build war factory → build radar → unlock X" progression. Tech levels exist (`~techlevel.low/medium/high`) but they are granted automatically based on game time or other conditions, not by constructing specific buildings. Any unit the player's tech level allows can be called in immediately.

### Map-edge spawning
Units don't appear at the production building — they enter from the map edge nearest to the Supply Route's SpawnArea hint, then walk/fly across the map to the rally point. This means:
- Production has inherent travel time (far Supply Route = slow reinforcements)
- Enemy can ambush reinforcements en route
- Supply Route position matters strategically (closer to friendly edge = safer reinforcements)

### Engine code still has old RA patterns
Many engine files still contain classic RA assumptions (e.g., `HasAdequateAirUnitReloadBuildings` checking for 1 airpad per aircraft). When you encounter these patterns, understand they may not apply. Always check how WW3MOD actually uses the system before assuming the old logic is correct. The `SkipRearmBuildingCheck` YAML property on `UnitBuilderBotModule` was added specifically to bypass one such legacy check.

## Workflow Rules

### Git & Commits
- **NEVER push to remote.** The user will push manually.
- **Commit regularly without asking.** Frequent small commits are preferred over batched changes.
- Create descriptive commit messages. No need to ask before committing.
- **ALWAYS commit before ending a session.** Never leave uncommitted changes behind. If you made code changes, commit them — even if you didn't run FINALIZE. This is the #1 most important workflow rule.

### Communication Format
- **Occationally insert a seperate line with a red alert (1 or 2) in game unit phrase as if that unit was commenting on what is going on right now** It should connect to what you're about to do or what the user said — not random. No repeats within a session. Occasionally roast the user or go dark-humor.

- Every response MUST end with exactly two lines:
  ```
  TASK: <one-line description of what was tasked>
  TLDR: <one-line description of what was done>
  ```

### Self-Updating Instructions
- **Continuously update this CLAUDE.md** when you receive new information that makes current content obsolete.
- Do this without asking first.
- After updating, write a condensed summary of what changed at the end of the response (before TASK/TLDR).

### Session Workflow
On session start:
1. Read `CLAUDE/HOTBOARD.md` and scan `CLAUDE/DISCOVERIES.md` for recent entries
2. Glob `CLAUDE/sessions/active_*.md` — if any exist, read them (may be a parallel agent). Note their intended files to avoid conflicts
3. Write `CLAUDE/sessions/active_<YYMMDD_HHMM>_<topic>.md` before making code changes (task summary, intended files, status: in-progress)

During session:
- Unrelated bugs found → append to `CLAUDE/bugs/discovered.md`
- Non-obvious insights → append to `CLAUDE/DISCOVERIES.md` (dated)
- Stable patterns that apply broadly → also add to this CLAUDE.md

### Completion Bell
Ring the terminal bell (`printf "\a"`) when a significant task is complete, so the user knows to check back.

## Commands

User can type these keywords to trigger specific workflows. Commands are **uppercase** for clarity.

### PLAN
Enter planning mode for a new feature or change.
1. Ask the user clarifying questions — keep asking until the user says the plan is sufficient
2. During planning, research relevant code (read files, grep for patterns, check existing systems)
3. Write the final plan to `CLAUDE/plans/<YYMMDD>_<topic>.md` with:
   - Goal, constraints, affected files, step-by-step implementation, risks/open questions
4. Summarize the plan in chat and wait for approval before implementing

### FINALIZE
Mandatory wrap-up routine. Run after completing any feature/fix.
1. `printf "\a"` — ring the bell
2. Double-check work against `CLAUDE/DISCOVERIES.md` — ensure nothing was violated
3. Update `CLAUDE/HOTBOARD.md` — refresh active concerns, recent wins, stats
4. Promote session file: rename `active_*.md` → `CLAUDE/sessions/<YYMMDD>_<topic>.md`
5. Update `CLAUDE/BACKLOG.md` — add deferred items, mark completed with `[x]`
6. Auto-commit all changes (descriptive message)
7. Review this CLAUDE.md — new pattern? Structural change? Recurring gotcha? Update if yes

### TESTING
Prepare for a playtest session.
1. Build the project (`./make.ps1 all`)
2. List what to test — pull from HOTBOARD active concerns and recent changes
3. Write a testing checklist to `CLAUDE/plans/<YYMMDD>_testing_<topic>.md` with:
   - What to test, expected behavior, edge cases to try, what to look for
4. After user reports results, capture findings (bugs → `CLAUDE/bugs/discovered.md`, tuning notes → HOTBOARD)

### STATUS
Quick orientation on where things stand.
1. Read `CLAUDE/HOTBOARD.md`, recent git log (5 commits), and any `active_*.md` sessions
2. Print a concise summary: what was last worked on, what's active now, what's next

### BUGFIX <description>
Structured bug investigation.
1. Reproduce understanding — ask user for repro steps if not provided
2. Research: grep for relevant code, read related files, check DISCOVERIES.md for known patterns
3. Form hypothesis, implement fix
4. Add to `CLAUDE/bugs/discovered.md` if found while working on something else
5. If the bug reveals a new gotcha, add to DISCOVERIES.md and consider adding to Common Pitfalls in CLAUDE.md

### REVIEW
Review recent changes for quality.
1. `git diff HEAD~N` (ask user for range, default last commit)
2. Check for: common pitfalls (see below), leftover debug code, YAML formatting issues, missing condition wiring
3. Report findings, fix issues with user approval

## CLAUDE/ Folder

Claude's workspace for session tracking, plans, discoveries, and notes. Primarily for Claude's use across sessions, secondarily for user reference.

```
CLAUDE/
├── HOTBOARD.md          # Live dashboard (max 40 lines, rotate old items)
├── BACKLOG.md           # Deferred tasks & ideas ([ ]/[x]/[dropped])
├── DISCOVERIES.md       # Dated gotchas and insights
├── plans/               # Plan documents (from PLAN command)
├── sessions/            # Session logs (active_* = in-progress)
└── bugs/
    └── discovered.md    # Bugs found incidentally
```

**Rules:**
- HOTBOARD.md stays under 40 lines — rotate oldest items out
- Session files: write `active_*` on start, promote to dated file on FINALIZE
- Never delete another agent's `active_*` file
- DISCOVERIES.md entries are always dated
- BACKLOG.md uses `[ ]` pending, `[x]` done, `[dropped]` irrelevant
- No duplication between CLAUDE/ files and auto-memory (`.claude/projects/`)

## Build & Run

```bash
# Windows (PowerShell)
./make.ps1 all          # Full build (targets net6, but runs on .NET 8+)

# Linux/macOS
make all

# Run the game
./launch-game.sh        # or launch-game.cmd on Windows

# Test (YAML validation)
make test               # Note: requires .NET 6 runtime specifically
```

The solution file is `WW3MOD.sln`. The engine compiles to `engine/bin/`. Mod DLL is `OpenRA.Mods.WW3MOD.dll` (referenced in mod.config but not yet a separate project).

### MCP Map Server
```bash
cd tools/map-mcp && npm install && npx tsc   # Build
```
Configured in `.mcp.json`. Provides 17 tools for map creation/editing: `create_map`, `read_map`, `list_maps`, `fill_terrain`, `paint_terrain`, `get_tileset_info`, `place_actors`, `remove_actors`, `list_actor_types`, `set_players`, `set_spawn_points`, `set_map_rules`, `write_lua_script`, `generate_preview`, `place_template`, `draw_road`, `auto_shore`.

### Combat Balance Simulator
```bash
cd tools/combat-sim && npm install && npx tsc   # Build
node build/index.js run <scenario>               # Run scenario
node build/index.js duel abrams t90 --range 18c0 # Quick 1v1
node build/index.js list                          # List scenarios
node build/index.js units                         # List units
node build/index.js stats <unitId>                # Unit details
```
Tick-by-tick combat simulator for balance analysis. Models damage (penetration, directional armor, range falloff, AoE), weapon firing cycles, suppression (infantry 10-tier/vehicle 5-tier), and formations. Phase 1 uses hardcoded stats; Phase 2 will auto-load from YAML. Phase 5 will export scenarios as playable maps via MCP.

## Project Architecture

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
│   ├── maps/                       # 13 maps (snow, temperate, urban)
│   ├── bits/                       # Sprites, sounds, models
│   ├── chrome/                     # UI layouts
│   ├── sequences/                  # Animation definitions
│   └── mod.yaml                    # Mod manifest
├── DOCS/                           # Documentation
│   ├── TODO.md                     # Prioritized v1 release TODO (Tier 1/2/3)
│   ├── KNOWN_BUGS.md               # Active and fixed bugs
│   ├── IDEAS.md                    # Feature ideas (collected from old notes)
│   ├── CLAUDE_IDEAS.md             # Claude's own feature suggestions
│   ├── SPRITE_REFERENCES.md        # Asset references from other OpenRA mods
│   ├── UnitManagement.md           # Group Scatter docs and unit control ideas
│   └── PROJECT_ASSESSMENT.md       # Comprehensive project assessment (March 2026)
├── tools/                          # Development tools
│   └── map-mcp/                    # MCP Map Creation Server (TypeScript/Node.js)
│       ├── src/index.ts            # MCP server with 14 map editing tools
│       ├── src/map-bin.ts          # Binary map.bin reader/writer (format 2)
│       ├── src/map-yaml.ts         # MiniYaml map.yaml reader/writer
│       └── src/tileset.ts          # Tileset definition parser
├── .mcp.json                       # MCP server configuration
├── CLAUDE.md                       # This file
├── WW3MOD.sln                      # Visual Studio solution
├── Makefile / make.ps1             # Build system
└── mod.config                      # Build configuration
```

## Scenario System

Scenarios are scripted map variants that share terrain with a base map but add different units, players, and Lua scripts. They appear in the lobby map chooser under the "Scenario" category.

### How It Works
- A scenario is a **separate map folder** that copies `map.bin` (terrain) from a base map
- Has its own `map.yaml` (different actors, players), `rules.yaml` (LuaScript reference), and `.lua` script
- Uses `Categories: Scenario` to appear in the Scenario filter in the map chooser
- No engine C# changes needed — everything runs on OpenRA's existing Lua scripting API
- Supports multiplayer + bots — human players take specific slots, bots fill the rest

### Creating a Scenario
1. Create a new map folder: `mods/ww3mod/maps/<base-map>-<scenario-name>/`
2. Copy `map.bin`, `shadows.bin`, `map.png` from the base map
3. Write `map.yaml` with:
   - `Categories: Scenario` and `LockPreview: True`
   - Custom players (human playable + non-playable garrison/AI factions)
   - Pre-placed actors (garrison units, supply routes, objectives)
4. Write `rules.yaml` with `LuaScript: Scripts: scenario.lua, <your-script>.lua`
5. Write your scenario `.lua` script using the `Scenario` helper library

### Scenario Lua Library (`mods/ww3mod/scripts/scenario.lua`)
Reusable helpers for scenario scripts:
- **Spawning**: `Scenario.SpawnUnit()`, `Scenario.SpawnGroup()`, `Scenario.ReinforceFromEdge()`
- **Ownership Transfer**: `Scenario.TransferAll(from, to)`, `Scenario.ScheduleTransfer(from, to, delaySec)`
- **Wave Spawning**: `Scenario.ScheduleWave(wave, delaySec)`, `Scenario.ScheduleWaves(waves, base, interval)`
- **Patrol/Defense**: `Scenario.Patrol(actors, waypoints)`, `Scenario.DefendPosition(actors)`
- **Messaging**: `Scenario.Message(text)`, `Scenario.SetBriefing(text)`, `Scenario.PlaySpeech(player, notif)`
- **Objectives**: `Scenario.AddPrimaryObjective(player, desc)`, `Scenario.CompleteObjective(player, id)`
- **Utility**: `Scenario.GetLiving(tag)`, `Scenario.CountLiving(tag)`, `Scenario.OnGroupEliminated(tag, cb)`

### Naming Convention
Scenario titles follow the format **`<Scenario>: <Map Name>`** — scenario name first, then the base map. This lets the same scenario type apply across multiple maps (e.g., "Frontline: River Zeta WW3", "Frontline: Siberian Pass WW3"). Feels like a game mode.

### Example: Frontline: River Zeta WW3 (`maps/river-zeta-frontline/`)
- 2 human player slots (NATO west, Russia east) each with a Supply Route
- NATOGarrison / RussiaGarrison — allied non-playable players with frontline troops
- After 3 minutes, garrison units transfer to human player control
- Enemy reinforcement waves (5 on Normal, scaling with difficulty dropdown)
- Difficulty dropdown: Easy (3 waves, 60% strength), Normal (5 waves), Hard (7 waves, 140% strength)

### Key Lua APIs Used
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

## Key Engine Modifications

These are the custom systems that set WW3MOD apart from base OpenRA. Understanding these is critical before modifying any engine code.

### Renamed/Rewritten Core Systems
| Original → Custom | Purpose |
|---|---|
| Shroud.cs → MapLayers.cs | Complete vision/shroud rework with graduated visibility |
| ShroudRenderer → MapLayersRenderer | Rendering for new vision system |
| Crushable.cs → Passable.cs | Richer obstacle interaction (fences, mines, trees) |
| TakeCover.cs → InfantryStates.cs | Infantry behavior model (prone at suppression > 30) |
| AffectsRadar → Radar.cs + Detectable.cs | Multi-layer detection/visibility |
| RadarWidget → MiniMapWidget | Renamed + reworked minimap |

### Custom Traits (new files)
| Trait | Purpose |
|---|---|
| Detectable.cs | Graduated visibility (cloaked/spotted/revealed) with additive modifiers |
| BlocksSight.cs | Objects that block line of sight |
| Radar.cs | Custom radar detection with range/conditions |
| ShockwaveDamageWarhead.cs | Explosive blast wave effects |
| InfantryStates.cs | Infantry states replacing TakeCover |
| EjectOnHusk.cs | Crew ejection from destroyed vehicles |
| GarrisonManager.cs | Shelter/port deployment model: soldiers enter shelter (Cargo), deploy to ports in-world when targets appear. DeployToPort/RecallToShelter, suppress flag, condition granting, INotifyKilled |
| GarrisonProtection.cs | Damage pass-through to shelter occupants only (port soldiers have DamageMultiplier via garrisoned-at-port condition) |
| WithGarrisonDecoration.cs | Empty port indicators only (deployed soldiers have their own in-world pips/decorations) |
| GarrisonPanelLogic.cs | Sidebar panel for garrison management (shows deployed + shelter soldiers) |
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
| HeliEmergencyLanding.cs | Helicopter emergency landing: autorotation on heavy damage (steerable, safe landing), uncontrolled crash on critical (spinning, destroyed). Crew ejection gated by terrain suitability |
| CargoSupply.cs | Supply as numeric cargo weight: any transport with this trait auto-rearms nearby units. Supply consumes Cargo weight (1 unit = 1 weight). Replaces SupplyProvider on TRUK |
| CargoPanelLogic.cs | Sidebar panel for transport cargo management: individual eject, mark for waypoint unload, rally points, supply drop |
| CargoUnloadOrderGenerator.cs | Click-on-map order generator for waypoint-based selective unloading of marked passengers |
| EjectRallyOrderGenerator.cs | Click-on-map to set per-passenger post-eject rally point (move target on ejection) |

### Heavily Modified Systems
| File | Key Changes |
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

## Aircraft Movement System

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

### Fixed-Wing (CanSlide = false)
Use traditional step-based movement:
- `Aircraft.FlyStep()` — returns movement vector for current speed/facing
- `Fly.FlyTick()` — applies movement, handles altitude, roll, pitch
- Turns computed via `Fly.CalculateTurnRadius()`

### Key Aircraft YAML Properties
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

## WDist Notation

OpenRA uses `WDist` (World Distance) units throughout. The notation is `NcXXX`:
- `1c0` = 1 cell = 1024 WDist units
- `4c0` = 4 cells
- `1c512` = 1.5 cells (1024 + 512 = 1536)
- `3c768` = 3.75 cells
- `0c512` = 0.5 cells
- Plain numbers like `512` = 512 WDist units (half a cell)

## WAngle Facing Convention

OpenRA uses `WAngle` for facings with **counterclockwise** rotation (0–1024 range). This is the OPPOSITE of typical clockwise conventions — easy to get wrong!

| WAngle | Direction | Screen Direction (top-down) |
|--------|-----------|---------------------------|
| 0      | North     | Up                        |
| 256    | **West**  | **Left**                  |
| 512    | South     | Down                      |
| 768    | **East**  | **Right**                 |

**Quick reference for map placement:**
- Units on the LEFT side facing right: `Facing: 768` (East)
- Units on the RIGHT side facing left: `Facing: 256` (West)
- Conversion: `WAngle.FromFacing(oldFacing)` where old RA facing × 4 = WAngle

## Suppression System (Complete)

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
- Defensive: fire from current position, reposition only if LOS blocked (Phase 2: cover-seeking via ShadowLayer)
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

## YAML Conventions

### Templates (prefixed with ^)
```yaml
^Aircraft:          # Base template for fixed-wing planes
^Helicopter:        # Base template for helicopters
^Drone:             # Base template for drones
^Airborne:          # Common airborne traits
^NeutralAirborne:   # Airborne without faction-specific traits
^AirRadar:          # Radar trait for aircraft (range 24c0)
```

### Conditions System
Traits grant and consume named conditions:
```yaml
GrantConditionOnDamageState:
    Condition: heavy-damage-attained    # Granted at heavy damage
SpeedMultiplier@HeavyDamage:
    Modifier: 90
    RequiresCondition: heavy-damage-attained
```

Common conditions: `airborne`, `cruising`, `moving`, `empdisable`, `dronedisable`, `heavy-damage-attained`, `critical-damage`, `rank-veteran`, `suppression-*`, `unit.docked`

### Faction-Specific Files
Each unit type has a base template file and two faction files:
- `aircraft.yaml` → `aircraft-america.yaml` + `aircraft-russia.yaml`
- `infantry.yaml` → `infantry-america.yaml` + `infantry-russia.yaml`
- `vehicles-america.yaml` + `vehicles-russia.yaml`

## Current State & Priorities (March 2026)

### Recently Completed
- P0 bugs fixed (Nuclear Winter crash, River Zeta crash, TECN infiltration)
- Dev branch merged (68 commits: doubled HP, critical at 50%, shroud smoothing)
- Air branch merged with fixes (velocity movement, deceleration, fixed-wing restored)
- Modifiers branch merged (attack-move modifier keys)
- AI improved (balanced builds, aircraft enabled, repair/capture modules)
- Aircraft costs restored (were all set to 1 for testing)
- **Suppression system completed** — vehicle suppression added (turret slow + inaccuracy, medium/large/huge caliber only), infantry suppression tuned
- **Stances system implemented** — 3+3 system: Fire discipline (HoldFire, Ambush, FireAtWill) + Engagement (HoldPosition, Defensive, Hunt). Ambush pre-aims and coordinates nearby allies
- **Vehicle reverse movement** — tracked (50 ticks, 40% speed) and wheeled (30 ticks, 60% speed) can reverse instead of 180° turns
- **Supply Route edge spawning** — units now spawn at map edge and march to rally point; buildings/defenses still spawn locally
- **Bleedout animation fixed** — rot sequences now use existing e1 sprite frames instead of missing rot1-4/corpse1-3 files
- **Helicopter movement precision fixed** — corrected physics formulas (semi-implicit Euler), eliminated double movement in Land/FlyIdle, precise position snapping on arrival, velocity zeroing on all Fly exit paths, maintenance acceleration to prevent speed oscillation
- **Supply Route defeat behavior fixed** — SR turns neutral on owner defeat instead of being killed; ProximityCapturable no longer permanently locks when owner is defeated
- **Rotate/sell to map edge** — units ordered to rotate via Supply Route now walk to the map edge (biased toward SpawnArea), not to the SR building. SR acts as a proxy target
- **Vehicle reverse sliding fixed** — reverse condition re-evaluated at each cell transition; stops reversing when path curves away from behind the unit
- **Group Scatter hotkey (Alt+S)** — distributes queued waypoints among selected units by type (inspired by Supreme Commander FAF). See `DOCS/UnitManagement.md`
- **Garrison system: shelter/port deployment** — Infantry enter building shelter (Cargo), GarrisonManager deploys best-matched soldier to port in-world when targets appear. Deployed soldiers are in-world with garrisoned-at-port condition (sprite hidden, pips visible, selectable, targetable). Port soldiers get 80% damage reduction via DamageMultiplier. GarrisonProtection passes damage only to shelter soldiers. WithGarrisonDecoration simplified to empty port indicators. Building death: port soldiers become free infantry, shelter soldiers ejected by Cargo
- **Supply truck → pure transport** — TRUK no longer has SupplyProvider/QuickRearm/DropsCrate. Now uses CargoSupply trait: supply as numeric cargo weight (1 unit = 1 weight, InitialSupply: 10). Any transport with CargoSupply (TRUK, Chinook, HALO, HIND) auto-rearms nearby units when carrying supply. SUPPLYCACHE is now NoAutoTarget + ProximityCapturable (1.5 cell, sticky ownership transfer)
- **Cargo management system (Phases 2A-2E)** — Full transport cargo UI: individual passenger eject, mark passengers for waypoint-based selective unload (Deploy Marked → click map → transport moves+unloads), per-passenger rally points (R button → click map → passenger auto-moves there on ejection), supply unload as SUPPLYCACHE with merge at same location, Mark All/Unmark All toggle
- **Medic/Engineer smart auto-targeting** — HealerClaimLayer prevents medic pile-ups (1:1 healer→patient claims). HealerAutoTarget (IOverrideAutoTarget) scores by HP%, prioritizes critical patients (<50%, bleeding out), stabilizes to 50% then switches. Engineers use same trait without claims (multiple can repair same vehicle)
- **Vehicle crew system** — VehicleCrew trait manages Driver/Gunner/Commander slots. On critical damage (<50% HP), crew eject one-by-one as infantry with vehicle's rank. Missing crew progressively disables systems (no driver=immobile, no gunner=turret frozen, no commander=inaccuracy). Commander substitutes at reduced efficiency. Crew can re-enter repaired vehicles via CrewMember trait. 14 vehicles configured (2-crew light vehicles, 3-crew MBTs/IFVs). Crew evacuated via supply route return 100 credits each
- **Infantry mid-cell redirect** — Infantry can now change direction mid-cell instead of finishing their current cell transition before responding to new move orders. MovePart made conditionally interruptible via `CanRedirectMidCell` on MobileInfo. On cancel, reverts cell occupancy to FromCell and starts new move from actual WPos (no visual snap). Sharp direction changes (>90°) apply a speed penalty scaling from 100% to `RedirectSpeedPenalty`% at 180°. Vehicles left unchanged (finish cell transitions as before)
- **Three-mode move system** — Move (right-click): SmartMove wrapping fires only in self-defense (under fire) or when target isn't already saturated with incoming damage (overkill check via AverageDamagePercent). Attack-Move (A+click): unchanged, fires at everything. Force-Move (Ctrl+click): pure movement via "ForceMove" order string, bypasses SmartMove wrapping entirely
- **Stance system consolidated to 3+3** — Removed redundant stances (ReturnFire, Defend, AttackAnything, Balanced). Fire discipline: HoldFire/Ambush/FireAtWill. Engagement: HoldPosition/Defensive/Hunt. 6 total buttons (was 9). All enums, conditions, UI, hotkeys, and YAML updated across engine and all mods. Phase 2 (shadow-based cover seeking for Defensive) planned in `DOCS/SHADOW_LOS_PLAN.md`
- **Control bar overhaul** — Added Cohesion (Tight/Loose/Spread) and Resupply Behavior (Hold/Auto/Evacuate) stance bars. Click-modifier meta-system on all 4 bars: Click=set stance, Ctrl+Click=per-unit default, Ctrl+Alt+Click=per-type default, Alt+Click="Do Now" order (persisted across games via UnitDefaultsManager). Evacuate command button removed (folded into Resupply bar). Tooltips anchor above buttons. Medic/Engineer default to Hunt engagement. Resupply behavior implemented: Auto seeks supply, Hold flags for truck pickup, Evacuate leaves via SR. Supply trucks in Hunt stance seek flagged units map-wide. Cohesion distributes group move targets via IModifyGroupOrder (CohesionMoveModifier). Patrol system with waypoint queuing (PatrolOrderGenerator → PatrolActivity bounce/circular loop)
- **Supply Route contestation system** — Replaced binary ProximityContestable with graduated SupplyRouteContestation trait. Control bar (0-100%) depleted by net enemy value surplus in 10-cell range: 5 infantry (~2500 value) depletes in 60s, full company in 20s min. Production speed scales linearly below 50% bar (100%→0%), halts at 0%. Auto-recovery when enemies leave, 3x faster with friendlies. Full feedback: player-colored selection bar visible to all, building flash, EVA "BaseAttack" notification, text log, minimap ping. New IProductionSpeedModifier interface with accumulator pattern in ProductionQueue for dynamic per-tick speed control. SR is indestructible — enemies can only deny, never capture
- **Helicopter emergency landing system** — Two-tier: Heavy damage triggers controlled autorotation (player steers, helicopter glides forward losing altitude, lands safely on suitable terrain as disabled+repairable unit, crew/passengers evacuate). Critical damage triggers uncontrolled crash (configurable spinning for tail rotor loss, always destroyed on impact, crew ejected only if on suitable ground terrain). Mid-air destruction = crew dies (suppress-eject condition gates EjectOnDeath). Chinook/HALO: SpinsOnCrash=false for dual rotors. RepairableBuilding activated on crash-disabled condition for ground repair
- **Scenario system** — Map scenario variants sharing terrain (map.bin) with different actors/scripts. Reusable `scenario.lua` Lua library (spawn, ownership transfer, waves, patrol, objectives, messaging). First scenario: "River Zeta — Frontline" — garrison forces + timed transfer + enemy waves + difficulty dropdown. No engine C# changes — pure Lua + YAML. `Categories: Scenario` for map chooser filtering

### Next Priorities
1. **Helicopter emergency landing playtesting** — verify autorotation descent, crash spinning, safe landing repair flow, crew ejection on suitable terrain, crew death on mid-air destruction, Chinook no-spin
2. **Stance system playtesting** — verify modifier system (Click/Ctrl/Ctrl+Alt/Alt), resupply behavior (Auto seek, Hold flag, Evacuate via SR), medic/engineer Hunt default, tooltips anchored above, cohesion distribution on group moves, patrol waypoint queuing and looping
3. **Supply Route contestation playtesting** — verify bar depletes/recovers, production slowdown, notifications
4. **Shadow falloff + firing LOS** — distance-based shadow falloff, Defensive stance cover-seeking. See `DOCS/SHADOW_LOS_PLAN.md`
5. **Three-mode move playtesting** — tune OverkillThreshold, UnderFireDuration, verify Force-Move never fires
6. **Infantry mid-cell redirect playtesting** — tune RedirectSpeedPenalty (currently 50%), verify no visual glitches
7. **Vehicle crew playtesting** — tune ejection delays, commander substitution values, test re-entry flow
8. **Cargo system playtesting (Phases 2A-2E)** — verify TRUK auto-rearms, supply bar, individual eject, mark+waypoint unload, rally points, supply drop to SUPPLYCACHE, merge at same location
9. **Cargo management Phase 3** — template sidebar for pre-loaded transport purchasing
10. **Garrison shelter/port playtesting** — verify deploy-to-port behavior, pips, building death
10. **Suppression tuning** — playtest and balance vehicle suppression values, per-weapon fine-tuning

### Remaining Branches
- `skane`/`xavi` — cherry-pick useful parts (map data, sprites)
- `maps` — extract useful maps
- `bypass`, `counterbattery`, `speed` — stale, can likely delete

### Engine Upgrade Consideration
Upgrading to `release-20250330` is possible but major (estimated 12-22 sessions). The recommendation is to finish gameplay features first, then consider a selective backport or full upgrade. See `DOCS/PROJECT_ASSESSMENT.md` Section 5 for full analysis.

## AI Configuration

AI is configured entirely via YAML in `mods/ww3mod/rules/ai/`:
- `ai.yaml` — ModularBot setup, shared modules (BuildingRepairBotModule, CaptureManagerBotModule, SquadManagerBotModule for air, HelicopterSquadBotModule)
- `ai-america.yaml` — America-specific build priorities, unit limits, squad composition
- `ai-russia.yaml` — Russia-specific same

Key AI modules: `UnitBuilderBotModule` (what to build), `SquadManagerBotModule` (how to attack), `HelicopterSquadBotModule` (helicopter attack/scout/transport squads), `CaptureManagerBotModule` (what to capture), `BuildingRepairBotModule` (auto-repair).

**Important for aircraft modules:** Helicopter `UnitBuilderBotModule` uses `SkipRearmBuildingCheck: true` because helicopters are called in via Supply Route and don't need an HPAD to be produced. Without this flag, the old RA check (`HasAdequateAirUnitReloadBuildings`) blocks aircraft production when no rearm building exists.

## Testing

Unit tests live in `engine/OpenRA.Test/` (NUnit 3). Run with:
```bash
dotnet test engine/OpenRA.Test/OpenRA.Test.csproj --configuration Release
```

**WW3MOD-specific tests** (added March 2026):
- `AmmoPoolTest.cs` — GiveAmmo/TakeAmmo clamping, initial ammo, SupplyValue, FullReloadTicks math
- `SupplyProviderMathTest.cs` — distance-based delay formula, supply deduction, selection bar
- `SuppressionMathTest.cs` — infantry/vehicle tier progressions, decay timing, caps, prone threshold

**Dev helper script:** `./ww3-dev.ps1` — build, run, test, pre-flight checks, debug log cleanup

**Note:** Build fails if the game is running (DLLs locked). This is normal — the user often playtests while agents work. If a build fails for this reason, just move on to other work or wait quietly. Do not speculate, alarm, or ask the user to close the game. `launch-game` auto-builds before launching, so the user will get a fresh build on next playtest.

## Common Pitfalls

1. **Don't leave Console.WriteLine in engine code** — fires every tick, causes massive log spam
2. **Aircraft Cost: 1** — test values left in YAML. Always verify costs after air branch changes
3. **CanSlide vs non-CanSlide** — Fly.Tick has fully separate code paths. CanSlide sets RequestedAcceleration only (Aircraft.Tick moves via CurrentVelocity). Fixed-wing uses FlyTick (step-based). NEVER use FlyTick for CanSlide without zeroing CurrentVelocity first — causes double movement
4. **SeedsResource on maps without IResourceLayer** — causes crashes. Disable or remove SeedsResource actors
5. **FrozenActor.Actor can be null** — always null-check before accessing after superweapons
6. **YAML blank lines matter** — templates must be separated by blank lines
7. **Engine changes are in-repo** — no submodule, no separate DLL. Every C# edit touches OpenRA source directly
