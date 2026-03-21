# WW3MOD - Agent Instructions

## Identity

WW3MOD is a **total conversion** of OpenRA Red Alert into a modern World War 3 RTS. It is NOT a simple YAML mod — it rewrites 264 C# engine files (~6300 insertions, ~3300 deletions) on top of OpenRA `release-20230225`. The engine lives in-repo (not submodule), with `AUTOMATIC_ENGINE_MANAGEMENT="False"`.

**Authors:** FreadyFish (primary dev) & CmdrBambi
**Repository:** https://github.com/WW3MOD/2023.git
**Factions:** NATO/America vs BRICS/Russia (Ukraine planned as third)

## Workflow Rules

### Git & Commits
- **NEVER push to remote.** The user will push manually.
- **Commit regularly without asking.** Frequent small commits are preferred over batched changes.
- Create descriptive commit messages. No need to ask before committing.

### Communication Format
- **Start every response with a short Red Alert / C&C quote, reference, or joke.** One line max. It should connect to what you're about to do — not random. No repeats within a session. Draw from the full C&C universe: Red Alert 1/2/3, Tiberium series, Generals — unit lines, advisor quotes, cutscenes, loading screen tips. Assume deep franchise knowledge. Occasionally roast the user or go dark-humor. When the user quotes C&C, answer in-character from the same game.
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
- **Supply truck rework** — SupplyProvider trait replaces ProximityExternalCondition. Targeted single-unit resupply (closest first), distance-based reload speed, 500 supply capacity with auto-restock at logistics center. AmmoPool.SupplyValue for per-ammo-type cost balancing. Truck deploys into SUPPLYCACHE via Transforms
- **Medic/Engineer smart auto-targeting** — HealerClaimLayer prevents medic pile-ups (1:1 healer→patient claims). HealerAutoTarget (IOverrideAutoTarget) scores by HP%, prioritizes critical patients (<50%, bleeding out), stabilizes to 50% then switches. Engineers use same trait without claims (multiple can repair same vehicle)
- **Vehicle crew system** — VehicleCrew trait manages Driver/Gunner/Commander slots. On critical damage (<50% HP), crew eject one-by-one as infantry with vehicle's rank. Missing crew progressively disables systems (no driver=immobile, no gunner=turret frozen, no commander=inaccuracy). Commander substitutes at reduced efficiency. Crew can re-enter repaired vehicles via CrewMember trait. 14 vehicles configured (2-crew light vehicles, 3-crew MBTs/IFVs). Crew evacuated via supply route return 100 credits each
- **Infantry mid-cell redirect** — Infantry can now change direction mid-cell instead of finishing their current cell transition before responding to new move orders. MovePart made conditionally interruptible via `CanRedirectMidCell` on MobileInfo. On cancel, reverts cell occupancy to FromCell and starts new move from actual WPos (no visual snap). Sharp direction changes (>90°) apply a speed penalty scaling from 100% to `RedirectSpeedPenalty`% at 180°. Vehicles left unchanged (finish cell transitions as before)
- **Three-mode move system** — Move (right-click): SmartMove wrapping fires only in self-defense (under fire) or when target isn't already saturated with incoming damage (overkill check via AverageDamagePercent). Attack-Move (A+click): unchanged, fires at everything. Force-Move (Ctrl+click): pure movement via "ForceMove" order string, bypasses SmartMove wrapping entirely
- **Stance system consolidated to 3+3** — Removed redundant stances (ReturnFire, Defend, AttackAnything, Balanced). Fire discipline: HoldFire/Ambush/FireAtWill. Engagement: HoldPosition/Defensive/Hunt. 6 total buttons (was 9). All enums, conditions, UI, hotkeys, and YAML updated across engine and all mods. Phase 2 (shadow-based cover seeking for Defensive) planned in `DOCS/SHADOW_LOS_PLAN.md`

### Next Priorities
1. **Stance system playtesting** — verify 3+3 system works: Hunt chases targets, Defensive repositions only on LOS block, HoldPosition never moves, all fire+engagement combos work, AI defaults to FireAtWill+Defensive
2. **Shadow falloff + firing LOS (Phase 2)** — distance-based shadow falloff from viewer, per-unit ClearSightThreshold for firing, Defensive stance cover-seeking using ShadowLayer. See `DOCS/SHADOW_LOS_PLAN.md`
3. **Three-mode move playtesting** — tune OverkillThreshold (100), UnderFireDuration (75 ticks), verify Force-Move never fires, verify regular move lets some units peel off while rest keep moving
4. **Infantry mid-cell redirect playtesting** — tune RedirectSpeedPenalty (currently 50%), verify no visual glitches on sharp redirects, test with garrison enter/attack orders
5. **Vehicle crew playtesting** — tune ejection delays, commander substitution values, test re-entry flow
6. **Supply truck playtesting** — tune range, delays, supply costs, restock behavior
7. **Garrison shelter/port playtesting** — verify soldiers deploy to ports when enemies appear, pips visible at port positions, direct selection works, force-move ejects, building death frees port soldiers, HoldFire prevents deployment, reload swap works between shelter and port
8. **Suppression tuning** — playtest and balance vehicle suppression values, per-weapon fine-tuning

### Remaining Branches
- `skane`/`xavi` — cherry-pick useful parts (map data, sprites)
- `maps` — extract useful maps
- `bypass`, `counterbattery`, `speed` — stale, can likely delete

### Engine Upgrade Consideration
Upgrading to `release-20250330` is possible but major (estimated 12-22 sessions). The recommendation is to finish gameplay features first, then consider a selective backport or full upgrade. See `DOCS/PROJECT_ASSESSMENT.md` Section 5 for full analysis.

## AI Configuration

AI is configured entirely via YAML in `mods/ww3mod/rules/ai/`:
- `ai.yaml` — ModularBot setup, shared modules (BuildingRepairBotModule, CaptureManagerBotModule, SquadManagerBotModule for air)
- `ai-america.yaml` — America-specific build priorities, unit limits, squad composition
- `ai-russia.yaml` — Russia-specific same

Key AI modules: `UnitBuilderBotModule` (what to build), `SquadManagerBotModule` (how to attack), `CaptureManagerBotModule` (what to capture), `BuildingRepairBotModule` (auto-repair).

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

**Note:** Game must be closed before building/testing (DLLs are locked while running).

## Common Pitfalls

1. **Don't leave Console.WriteLine in engine code** — fires every tick, causes massive log spam
2. **Aircraft Cost: 1** — test values left in YAML. Always verify costs after air branch changes
3. **CanSlide vs non-CanSlide** — Fly.Tick has fully separate code paths. CanSlide sets RequestedAcceleration only (Aircraft.Tick moves via CurrentVelocity). Fixed-wing uses FlyTick (step-based). NEVER use FlyTick for CanSlide without zeroing CurrentVelocity first — causes double movement
4. **SeedsResource on maps without IResourceLayer** — causes crashes. Disable or remove SeedsResource actors
5. **FrozenActor.Actor can be null** — always null-check before accessing after superweapons
6. **YAML blank lines matter** — templates must be separated by blank lines
7. **Engine changes are in-repo** — no submodule, no separate DLL. Every C# edit touches OpenRA source directly
