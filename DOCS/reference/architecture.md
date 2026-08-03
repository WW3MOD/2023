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

**The shadow/density layers are frozen at map load and never rebuilt at runtime.** `Map.DensityLayer` and `Map.ShadowLayer` are populated exactly once — from `shadows.bin` when present (`engine/OpenRA.Game/Map/Map.cs:469-495`), else a one-time `SetDensityLayer()`+`SetShadowLayer()` fallback after `PostInit` (`Map.cs:505-509`). The dynamic-recompute path (`Map.UpdateDensityForBuilding` / `QueueShadowUpdate` / `FlushPendingShadowUpdates`) was **disabled 260503** (too expensive mid-game — visible lag on building destruction); the `Building` add/remove density hooks that would mutate it are commented out (`Traits/Buildings/Building.cs:372-383` add, `:391-397` remove, with the dated reason inline). **Consequence:** killing a tree removes the actor and its sprite but leaves its density — and therefore its concealment / LOS blocking **and its damage-reduction cover** (`DensityModifiesDamage` samples the frozen `DensityLayer` over a per-cell window, `Infantry/DensityModifiesDamage.cs:72-87`) — baked in; burning or shelling a forest does **not** open sightlines or thin cover. Any "dynamic forest / deforestation opens lanes" idea is blocked until that 260503-disabled pipeline is re-enabled and its mid-game-lag cost solved.

## Asset pipeline: loose files & sprite loading

**Loose-file-over-mix precedence — the drop-in replacement rule.** `FileSystem` resolves a filename to the **last-mounted** package that contains it: `GetFromCache` uses `fileIndex[filename].LastOrDefault(...)` (`engine/OpenRA.Game/FileSystem/FileSystem.cs:195`), and the per-package fallback likewise uses `mountedPackages.Keys.LastOrDefault` (`:246`). In `mods/ww3mod/mod.yaml` the RA `.mix` archives mount first (`:21-37`) and the loose `ww3mod|bits*` dirs mount later (`:49-59`), so **a loose file shadows the mix copy of the same name** — dropping a rebuilt sprite in `mods/ww3mod/bits/` overrides the archived one with no manifest edit.
- **CAVEAT — the loose filename must match the sprite's *resolved* name, including tileset extension.** Trees/terrain sequences use `UseTilesetExtension: true` (`DefaultSpriteSequence.InferExtension`, `Graphics/DefaultSpriteSequence.cs:363-380`), so a `t01` tree resolves to `t01.tem` on TEMPERAT, `t01.sno` on SNOW, etc. A loose `t01.shp` will **not** shadow it — name the drop-in `t01.tem` (one file per tileset you care about). Units referenced by a plain `.shp` sequence shadow with a loose `.shp`.

**PngSheet — rendering loose PNGs.** Loose PNGs (both 8-bit indexed and 32-bit RGBA) render once `PngSheet` is in `SpriteFormats` (`mod.yaml:327`). The loader sniffs **content, not extension** (`Png.Verify` magic bytes, `SpriteLoaders/PngSheetLoader.cs:49`), so existing SHP/TMP sprites still load via their own loaders and a PNG-format file named e.g. `3tnk.shp` still renders as a PNG.
- **Indexed PNGs carry their OWN palette — no player-colour remap.** `PngSheetLoader` attaches an `EmbeddedSpritePalette` from the PNG's PLTE (`:87-88`), so an indexed PNG renders in its baked colours and ignores the actor's assigned palette / team-colour remap (indices 80–95). RGBA PNGs are pure truecolour (also no remap). For a team-coloured **unit**, use the SHP round-trip (pure indices, coloured by the runtime palette); loose PNGs suit decoration art.
- **Auto-sliced frames anchor CENTERED — the source SHP offset is lost.** With no embedded metadata, `RegionsFromSlices` treats the whole image as one frame with `Offset = float2.Zero` = sprite centre on actor centre (`:112-160`, offset default at `:136-142`); embedded `FrameSize`/`FrameAmount` chunks control slicing. So a SHP→PNG→PngSheet conversion **loses** the original trunk/turret anchor (a symmetric sprite looks right centred; an asymmetric one sits wrong). Fix anchoring via the sequence `Offset:` field or embedded `Offset`/`FrameSize`/`FrameAmount` chunks; the SHP round-trip preserves offset.

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
| CohesionMoveModifier.cs | World trait (IModifyGroupOrder): intent-aware cover-placement system for grouped Move/AttackMove orders. Classifies the target cell against `Map.DensityLayer` and dispatches to one of four slot strategies: **Open** (box layout — fires on open terrain, the typical AI case), **SpreadInside** (bid for top-CoverScore cells inside a cover patch), **EdgeLine** (line perpendicular to the density gradient, anchored at the cover edge), **Approach** (boundary-anchored line when the squad is far from a cover click). CohesionMode (Tight/Loose/Spread) controls only the **slot spacing** (col/row WDist), not which strategy fires. See `CohesionMoveModifier.cs:19-26` (class desc), `:126-143` (spacing), `:178-214` (intent classification). Fires for bot-issued grouped orders (confirmed: `UnitOrders.cs:397-413`). AI default is `Loose` (`AutoTarget.cs:120 InitialCohesionAI`). The **Open** box footprint is **bounded per mode**: once `(cols-1)*colSpacing` would exceed `{Tight,Loose,Spread}MaxWidth` (`:54-73`) the effective spacing shrinks to hold the span at the cap (same for depth), floored at `MinSlotSpacing` (1024) — so the box no longer grows without limit as unit count rises. Mode ordering Tight<Loose<Spread is preserved for every count. Regroup-on-arrival is not new code — the bound repurposes the existing `CohesionSlotMemory` sticky-slot leash. The **line strategies are width-capped too**: `EdgeLine`/`Approach` lay a line of width `(n-1)*colSpacing`, which was uncapped (a 12-unit Spread line spans 33 cells); `ModifyGroupOrder` now shrinks a per-order `lineColSpacing = min(colSpacing, maxWidth/(n-1))` floored at `MinSlotSpacing` (`:835-837`) before dispatching to the line intents, so a large group order no longer barely fits on screen. The **treeline classifier is anisotropy-based, not offset-based**: `ClassifyIntent` computes the density covariance's eigenvalues and routes elongated distributions (`λ1 ≥ TreelineMinSpreadSq && λ1 ≥ TreelineAnisotropyRatio·λ2`, `:302-313`; ratio 2.5) to `EdgeLine` laid *along* the major eigenvector — so a click centred *on* a treeline (centroid offset ≈ 0, which the old offset-magnitude test scattered as SpreadInside) is detected as a line. A round blob (`λ1≈λ2`) stays SpreadInside. `LayCoverAwareLine` lays perpendicular to its `forward` arg, so to string units *along* an axis `a` you pass `forward = (a.y, −a.x)` (`:555-558`). Slot assignment is a deterministic greedy nearest-slot matching (no `LocalRandom`, ActorID-then-slot-index tie-break); `PickCoverSlotNear` treats min-spacing as a *soft* constraint and bends the line into cover when a tidy slot has none (`:623`). **Order-time concealment (item 21):** for a human-owned Ambush-stance grouped move (`applyAmbushConcealment = isHuman && Stance == Ambush && mode != Tight`, `:1145`) each slot is nudged to the deepest nearby cover by `ConcealmentScore(cell) = Map.ForestGroundShadow(Σ density in a 5×5 window)` (`:338-346`). This is deliberately **viewer-independent**: the baked `Map.ShadowLayer` is indexed `[fromCell][toCell]` (`Map.cs:253`) and needs the enemy's cell — unknown at order resolution — so it scores how deep in cover a cell sits, not shadow along a real sightline. Returns 0 on open ground, degrading to the plain formation when no trees are near; bots default `FireAtWill` so never enter the branch (byte-identical). This is order-time only — the continuous idle repositioner `StancePositioningExecutor` *opts out* for Ambush/HoldFire (`StancePositioningExecutor.cs:318`), so an ambusher is placed once and left. |
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
| Move.cs | Path string-pulling: the 45° zig-zag is a chain of `IsTurn` boundary→boundary arcs (`:653`), so smoothing retargets each turn-chain arc's rendered `To` onto the sightline to the farthest visible waypoint (`PathStringPulling.cs`, applied via `SmoothSegmentTarget` `:379-398`). Only the rendered `WPos` moves — CPos occupancy/crush/reservation cadence keys on `FromCell`/`ToCell` (`Mobile.cs`) and is untouched, so the smoothing can't desync pathing. Gated to full-cell movers via `!LocomotorInfo.SharesCell` (`:370-374`) — NOT `AlwaysTurnInPlace` (commented out mod-wide, `infantry.yaml:48`); opt-in per unit (`StringPullMovement`, default false). Divergence hard-clamped to half a cell (`DefaultMaxDivergence = 512`); `LineOfWalkClear` is an Amanatides-Woo supercover DDA with a diagonal-corner guard. Trajectories change for all full-cell ground units incl. bots — determinism (integer/WDist, zero RNG) is the contract, not byte-identity. |
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
- InfantryStates triggers prone at suppression > 30. **Prone DOES grant concealment: +1 detection tier** via `DetectableAddativeModifier@Prone` (`RequiresCondition: prone`, `VisionModifier: 1`, `mods/ww3mod/rules/ingame/infantry.yaml:684-686`), applied inside `Detectable.IsVisibleInner` (`Detectable.cs:93-116`) — same mechanism as the `@Dugin` bonus. Beyond stealth, prone gives per-damage-type reduction, −40% speed, a smaller hitshape (r20 vs r30), and the crawl animation. (Corrected 2026-07-28; the doc previously claimed prone had no stealth effect.)
- 10-tier pip display (pip-suppression-1 through pip-suppression-10)
- **Suppression is not a blanket fire-halt** — suppressed infantry keep firing, just degraded by the multipliers above. The exceptions are three armaments that hard-pause via `PauseOnCondition: suppressed >= 10`: the AT Specialist ATGM (`infantry.yaml:1652`), the engineer mine-clear/repair arm (`:1865`), and the medic heal arm (`:2136`). So a "let it pass, then shoot the rear" AT ambush that lets the escort return fire risks suppressing its own AT gunner into silence before it exploits the rear arc.

**Vehicle suppression (5-tier, cap 50, decay 1/3 ticks):**
- Only medium caliber (12.7mm+) and explosions suppress vehicles
- Turret turn speed reduced (85%→25%), inaccuracy increased (115%→200%)
- Burst wait increased (105%→150%), NO speed reduction
- Defined in `^VehicleSuppressionEffects` template in vehicles.yaml

**Fire discipline (3 stances — controls WHEN to fire):**
- HoldFire, Ambush, FireAtWill (default)
- Ambush: pre-aim at targets, hold fire until spotted or damaged, coordinate with nearby allies. Widened into a stationary hide-and-spring state machine + an `@experimental` bot lane consumer behind the default-off `enable-ambush-tactics` gate — see §Widened ambush (Stages 1–4) below.
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

**The skirmish bot roster is exactly two profiles — `experimental` and `stable`** (`ModularBot@experimental` / `@stable`); the old Normal/Rush/Turtle bots were removed 2026-07-30. Two traps for anyone editing the config after the prune: (1) **`enable-ai-player` is the SHARED baseline tier**, granted to *both* surviving bots — so the `@normal`-named baseline blocks it gates (e.g. `BaseBuilderBotModule@normal`, `ai.yaml:566`) are live production for both bots, not dead legacy; the `@normal` suffix is now a misnomer, keep the blocks. (2) **`SkirmishLogic.cs` seeds the default AI opponent by string-matching the bot *type***: `availableBots.FirstOrDefault(t => t == "stable")` (`ServerTraits/SkirmishLogic.cs:182`), repointed from the removed `"normal"` — a fresh skirmish silently falls through to whatever bot is first if that string stops matching. The condition `enable-ai-legacy-only` is now granted to nobody and survives only in comments.

**Who commands ground units differs by profile.** The `@experimental` air `SquadManagerBotModule` sets `IgnoreGroundUnits: true` (`ai.yaml:629,692`), handing the entire ground pool to `PoiOffensiveBotModule`, which issues a **grouped `AttackMove` per axis** to the objective cell. So for `@experimental`, ground behaviour lives in `PoiOffensiveBotModule.CommitAndOrder`, **not** the `Squads/States/GroundStates.cs` FSM (a `GroundStates`-based change touches only the legacy/`@stable`/normal profiles that still let SquadManager own ground). Because the axis order is a plain `AttackMove`, indirect-fire pieces in the group march to contact and die — so `PoiOffensiveBotModule` can peel `IndirectFire` artillery off the group order and give it a bot-computed standoff move instead (default-off `FiresStandoff` flag, `PoiOffensiveBotModule.cs:215`; the ground analogue of the engine-provided aircraft standoff below). That standoff anchors the piece at its weapon range from the axis **TARGET**, which does not guarantee the friendly line sits between the piece and the enemy (an anchored flank, or advancing ahead of a slower screen, leaves it exposed). A further default-off `EchelonPositioning` (`:255`) instead holds each `IndirectFire` piece behind the axis's `MainBattle` **SCREEN** centroid by its range surplus over the screen — `depth = max(EchelonMinDepth, (ownMaxRange − screenRange) + EchelonBuffer)` (`EchelonMath.EchelonDepth`/`EchelonAnchor`) — falling back to the target-standoff whenever the axis has no screen (a pure-fires or deliberately-solo tasking), so "explicit tasking wins over the echelon bias" is structural, not a priority flag. A further default-off `FiresEvGate` (`ai.yaml:348`) adds an ammo expected-value gate to that fires loop: a **rocket**-type piece (`UnitRoleResolver` `IndirectFireKind.Rocket` — Grad/TOS/M270, derived from salvo `Burst`) holds fire while the best spotted clump in range would not repay the salvo's ammo cost (`RocketFireWorthy`, `PoiOffensiveBotModule.cs:1987`, over `FiresEconMath.SalvoCost`/`ProjectedClumpValue`) and returns to `FireAtWill` once a worthy clump appears; **tube** pieces (Giatsint/Paladin) are exempt and may engage singles. Orthogonally, the SHARED `AutoTarget` gains AoE **cluster targeting** (`ClusterTargetingCondition`, granted `enable-ai-experimental` at `defaults.yaml:395,413`): an area-warhead weapon earns a priority pull toward enemies clumped within `ClusterRadius` (`AutoTarget.cs:1141-1198`), so cluster munitions prefer massed targets. The whole fires doctrine (role split, standoff, echelon, EV gate, cluster targeting) ships and is enabled only under `@experimental`; two doctrine employments remain unbuilt — continuous bombardment of believed-static positions, and suppression-coordinated advance (no fires/offense module reads a target's suppression state) — designed in `WORKSPACE/plans/260803_fires_cycle_design.md`.

**Important for aircraft modules:** Helicopter `UnitBuilderBotModule` uses `SkipRearmBuildingCheck: true` because helicopters are called in via Supply Route and don't need an HPAD to be produced. Without this flag, the old RA check (`HasAdequateAirUnitReloadBuildings`) blocks aircraft production when no rearm building exists.

**Two more full-ammo gates brick attack-heli *squads* (separate from production).** WW3MOD attack helis rearm only at an HPAD and the mod builds none, so any heli that fired dips below full and never refills. That trips two independent launch gates: (1) `HelicopterSquadBotModule.IsReadyForMission` requires every `AmmoPool` at full for a heli with `AmmoPool`+`Rearmable` (`:607`, `HasFullAmmo` check at `:638`) — no squad ever *forms*; (2) the squad FSM's `SquadHasAmmo` (`Squads/States/HelicopterStates.cs:118-131`) *skips* every unit whose pools are all covered by a `Rearmable` (`ReloadsAutomatically` true — exactly attack helis) then returns false if none remain, so an all-attack-heli squad reports "no ammo" **even at full** and the idle/withdraw/re-engage gates never pass — the squad *forms but never launches*. Both are bypassed by `SkipRearmReadyCheck` (default-off, experimental only); the production `SkipRearmBuildingCheck` does **not** cover either. Corner-idle helis are arrival logic (`ProductionFromMapEdge` flies them to the SR/edge cell with no rally Path), not RA idle-return.

**Out-of-ammo evac is a unit-level `AmmoPool` behaviour, invisible to bot modules.** `AmmoPool.AutoRearmIfAllEmpty` `case Evacuate` queues `RotateToEdge` (`AmmoPool.cs:197-204`); WW3MOD vehicles opt in via `InitialResupplyBehaviorAI: Evacuate`. No bot module reads the resulting state, and the evac path never commits the unit to the `PoiGoalGuard` ledger — so an evacuating unit is "free" to any module that lacks an ammo filter and can be recruited back onto an axis, overwriting its retreat. `LayeredDefenceBotModule` is the only module that guards it (`SkipOutOfAmmoUnits`, default true, `:102,277`; `IsOutOfAmmo` = all pools at 0, `:469`). Any module pulling units by proximity/idle needs this guard or a shared evac reservation.

**This engine auto-evac never fires for AIRCRAFT.** `AutoRearmIfAllEmpty` hard-returns on `self.Info.HasTraitInfo<AircraftInfo>()` (`AmmoPool.cs:173`), and its `INotifyAttack` trigger guards on aircraft too (`:247`) — so no stance, including `Evacuate`, ever auto-rotates a spent heli to the edge. With no HPAD to rearm at, a spent attack heli `ReturnToBase`s and (nothing `Reservable`) `FlyIdle`s in place indefinitely, draining upkeep the whole time (`InfersUpkeep` charges from spawn until `RemovedFromWorld`, `InfersUpkeep.cs:83-89`). A heli therefore has **no engine evac path at all** — evacuating it (and thereby both banking the HP-scaled salvage via `RotateToEdge`'s `fixedRefund`, `RotateToEdge.cs:280`, and ending the upkeep drain) requires an explicit bot-module order, unlike a ground unit whose `Evacuate` stance handles it automatically.

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

**A player carries several disabled-but-constructed producer twins, and `TraitsImplementing<>` returns them — so routing a request to index `[0]` can silently drop it.** Each player has multiple `UnitBuilderBotModule` instances (per-profile `normal`/`experimental` plus per-aircraft `fixedwing`/`heli`), all but one condition-disabled per game via `RequiresCondition`. `PlayerActor.TraitsImplementing<IBotRequestUnitProduction>()` (and the `IBotRequestPriorityUnitProduction` variant) returns **all** of them — the disabled ones included — in trait construct order, which preserves the YAML/mod-load merge order among same-type traits. But `ModularBot` only *ticks* enabled modules (`ModularBot.cs:96`, `if (t.IsTraitEnabled()) t.BotTick(...)`) while the interface methods are invoked directly regardless of enabled state. So a requester that blindly uses `producers[0]` can land every request on a disabled twin whose `BotTick` never drains it: the unit is never built, yet `RequestedProductionCount` keeps counting the queued request, so the caller's `pending` climbs forever while `alive` stays 0 (a measured `pending=82 / alive=0` deadlock). Two in-tree fixes:

- **Route to the first *enabled* producer** via `Exts.FirstEnabledTraitOrDefault()` — the seam `McvManagerBotModule.cs:117` and `HarvesterBotModule.cs:158` already use. `AdaptiveProductionBotModule` gates this behind default-off `RouteToEnabledProducer` (`:93`; `SelectUnitProducer` `:269` delegates to the pure `AdaptiveRoutingMath.SelectProducerIndex`), set only on the two `@experimental` twins so `@stable` keeps the frozen `producers[0]` routing byte-identical (index 0 verbatim even when `[0]` is disabled).
- **An accept/reject handshake:** `IBotRequestPriorityUnitProduction.RequestPriorityUnitProduction` returns `false` when `IsTraitDisabled` (`UnitBuilderBotModule.cs:154-164`), and `CaptureCoordinatorBotModule` routes to the first twin that returns true (`:694`) — the request lands on the enabled UnitBuilder. That module's priority drain is peek-don't-pop (a busy queue keeps the request at the head for the next free slot) so delivery is non-lossy.

**Reactive counters come from `AdaptiveProductionBotModule`, not the static composition.** It scans the *fog-legal* enemy composition each cycle (`ScanEnemyComposition`, only actors passing `CanBeViewedByPlayer`, `AdaptiveProductionBotModule.cs:200,213`) and pushes threat-scaled requests through the same `IBotRequestUnitProduction` demand queue. AA is illustrative: it requests anti-air **only when `enemyAir > 0`** and caps at `aaCount < enemyAir*2` (`:145,149`). So the "several SHORAD/Tunguska sitting at the start" a player sees is **not** AdaptiveProduction over-reacting — with no enemy air sighted it requests zero AA. That early AA is the *static* `UnitsToBuild` share composition building toward its fixed weight regardless of threat. The two AA sources are independent: composition is the always-on baseline, AdaptiveProduction is the sighted-threat reactive top-up. The `@experimental` twin adds a separate **SR-defense** path (default-off `SupplyRouteDefenseEnabled`) that reads the *believed* belief store to classify an incoming rush by attacker **identity** and pre-buy the matched counter, bypassing `MinEnemySightings` — see [influence-stack.md](influence-stack.md#experimental-consumers-beyond-def).

### Adding a behavioural field to a trait shared by both bot profiles

`ModularBot@experimental` and `ModularBot@stable` (`ai.yaml:41-46`) share the same trait classes; `@stable` is the **frozen validated snapshot** used as a benchmark control. A new Info field with a non-baseline **code default** (e.g. `PoiOffensiveBotModule.ApproachCohesion = Spread`, `:96`) therefore leaks into `@stable` even when its YAML is untouched — silently mutating the control. Rule: **any behavioural Info field added to a shared trait must default to the frozen/baseline behaviour and be opted in per-profile via YAML.** The dispersion work does this with `CohesionSwitchEnabled` (default `false`, `:87`; the dispersion path is gated on it at `:424`), flipped `true` only on `@experimental`.

### All bot orders funnel through `ModularBot.QueueOrder`

Every `bot.QueueOrder(...)` call across the ~25 BotModules lands in the single interface-explicit `IBot.QueueOrder(Order)` on `ModularBot` (`Traits/Player/ModularBot.cs:91`), which just enqueues (`:97`); `ITick.Tick` dequeues (`:131`) and `world.IssueOrder`s each (`:137`). So any per-order instrumentation or attribution is a **single-file change, not 25**. The issuing module is not on the order, but it *is* known at the tick loop: `Tick` runs each `IBotTick` module via `if (t.IsTraitEnabled()) t.BotTick(this)` (`:111-116`; note **only enabled modules tick** — see §AI production's disabled-twin routing), and the attack-response path mirrors it (`:152-156`). Setting a `currentModuleTag = t.GetType().Name` field around those two loops and reading it in `QueueOrder` recovers per-order module identity for free.

### The bot free pool self-heals — only free-pool-EXCLUDED units can leak

`PoiOffensiveBotModule.BuildFreePool` (`:1316-1326`) scans **all** `world.Actors` (`:1321`) filtered by `IsEligibleCombatUnit` (`:1322`, requires `AttackBaseInfo` `:1439`, excludes aircraft), minus axis-claimed and ledger-committed units. Garrison/LaneAmbush/LayeredDefence/SquadManager use the same global scan. **Consequence:** an idle armed combat unit left anywhere on the map — even deep in contested territory after a drop — is re-collected regardless of location, so it is *self-healing*, not a leak. Only classes **excluded** from that pool can strand orderless at a hostile spot: when `UseUnitRoles` is on, `IsEligibleCombatUnit` (`:1455-1460`) admits only `MainBattle`/`IndirectFire` AND `!IsTroopCarrier`, so it excludes `ShortRangeAD`, `Recon`, `TransportLift`, `CaptureSpecialist`, `Logistics`, `AttackAir` (`UnitRole` enum, `Traits/World/UnitRoleResolver.cs:37-48`; `IsTroopCarrier` = has `Cargo` with `MaxWeight > 0`, `:239`). "Orderless-at-a-hostile-location" is therefore a bug **class** confined to dedicated non-combat units (a transport heli idling at its drop, a post-capture technician) — each needs an explicit return order; combat units do not.

### The PoiGoalGuard commitment ledger — commit-on-order + three-tier timers

`PoiGoalGuard` is a shared single-instance world module (fetched by consumers, gated `enable-ai-experimental || enable-ai-stable`) holding a per-unit commitment ledger that makes a claimed unit invisible to other axes/modules. Two invariants govern it:

- **Commit-on-order is a PAIR, not a single act** (`CommitOnOrderMath`, `PoiGoalGuard.cs:285-300`). Every executor must (a) commit every unit it orders AND (b) recruit only from the ledger-checked free pool — *both halves*. Commit-alone still lets a writer poach a unit another already committed (its own `Commit()` overwrites the prior objective); recruit-check-alone leaves the reverse steal channel open. The gate seams are `ShouldCommit(flag, ledgerAvailable)` (per-profile twin) and `ShouldCommitShared(flag, ledger, isExperimentalBot)` (adds a runtime `BotType == experimental` term for a module that is a single `enable-ai-any` instance, e.g. Garrison, which a per-profile YAML flag can't confine). Objective keys are **disjoint prefixes** so claims stay attributable and never collide: `offense:` / `capture:` / `capture-escort:` / `capture-defend:` / `transport:` / `garrison:` / `defend-line:<x>,<y>` (cell-based) / `ambush:`.
- **Three-tier timer ordering: `ReevaluateInterval` (100) < `AxisCommitmentTicks` (250) < `MissionCommitmentWindowTicks`.** The abort/reassign triggers are tested only at re-eval ticks (`PoiOffensiveBotModule` early-returns until a countdown hits 0, then re-evals every `ReevaluateInterval` `:57/:636-640`). Each re-eval a held axis re-asserts its ledger claim with a fresh `AxisCommitmentTicks` TTL (`:87`, `Ledger.Commit(..., AxisCommitmentTicks)` `:1245/:1518`). **If `ReevaluateInterval >= AxisCommitmentTicks` the claim lapses in the gap between two re-evals** and the unit is released mid-mission before any trigger can fire — the commitment window collapses to zero. `MissionCommitmentMath.ShouldReassign` (`PoiGoalGuard.cs:243`, NOT `ShouldRelease`) force-releases a held axis once `commitWindowTicks > 0 && currentTick − commitTick >= commitWindowTicks` (`:253-254`) — a bounded outer backstop that must sit ABOVE the ledger TTL so triggers get several samples first. The engine-class default `MissionCommitmentWindowTicks = 0` is **inert** (pure-trigger hold; `@experimental` sets 400 for ~3 held re-evals then a mandatory re-plan); all three fields live only on `PoiOffensiveBotModule@experimental`, so `@stable` (which omits them) is byte-identical.

### Widened ambush (Stages 1–4) — hide-and-spring + the bot lane consumer

The stock Ambush fire stance (above, "Fire discipline") is pre-aim-and-hold-fire-until-spotted. WW3MOD widens it into a stationary literal-ambush state machine plus an `@experimental` bot that actually posts ambushers on the enemy's reinforcement lane. The whole feature hangs off one **default-off gate condition, `enable-ambush-tactics`**, so every non-`@experimental` profile is byte-identical to stock.

- **The gate is granted by nobody in shipped rules; the seam is a sync-inert `ExternalCondition`.** `^AutoTarget` (`defaults.yaml:305`) declares `AutoTarget.AmbushTacticsCondition: enable-ambush-tactics` (`:315`) and an `ExternalCondition@ambushtactics` grantor for that token (`:331-332`). Nothing static grants it, so `self.GetConditionCount("enable-ambush-tactics") == 0` on every unit and the gated branches are dead code — the byte-identity guarantee. The `ExternalCondition` only makes the token *grantable* (satisfies `CheckConditions`; see conventions.md §Conditions) and carries no `[Sync]` state, so its mere presence draws no RNG and shifts no sync state.
- **OBS-1 — which units can host an ambush is a TEMPLATE-INHERITANCE fact, not a name list.** `^AutoTargetGround` (`defaults.yaml:553`) is a **separate base** from `^AutoTarget` (`:305`): it declares its own `AutoTarget:` block *without* `AmbushTacticsCondition` and *without* the `ExternalCondition@ambushtactics` seam. So the entire `^AutoTargetGround*` chain — AA IFVs (`^AutoTargetAAIFV` → `^AutoTargetGroundAntiTank` → … → `^AutoTargetGround`, `:364-365`) and every assault-move ground vehicle — has a null `AmbushTacticsCondition` and no grantable seam, and can never be an ambusher no matter what is granted. Units inheriting `^AutoTarget` (MBTs via `^AutoTargetMBT` `:334`, IFVs, artillery, most infantry) qualify. The Stage-4 filter `LaneAmbushBotModule.CanHostAmbush` (`LaneAmbushBotModule.cs:551-563`) tests exactly "non-empty `AmbushTacticsCondition` AND a grantable `ExternalCondition` for that token", so it excludes that family structurally — and self-heals the day someone wires the gate onto a new template.
- **Stage-2 halt-before-contact needs no new fire/spring code — it terminates the attack-move and lets the unit idle.** `AttackMoveActivity` (`Activities/Move/AttackMoveActivity.cs`) checks, when a gated Ambush-stance unit's attack-move scan finds an as-yet-unseen target, whether to halt (`AmbushTactics.ShouldHaltBeforeContact`, gated `Stance == Ambush && GetConditionCount(AmbushTacticsCondition) > 0`, `:123-128`); if so it latches `haltedForAmbush`, cancels the march, and drains the cancelling child so `Mobile` releases its cell reservation (`:79/:135-138`) — the unit drops to idle and `AmbushTickIdle` owns the ambush (silent pre-aim, hold-fire-until-spotted, damage retaliation, which only works *because* the unit is idle). Fork B is structural: a plain `Move` never wraps this activity, so only attack-move / bot auto-move can halt — a manual `Move` is always obeyed (`:122`).
- **Stage-3 stationary state machine lives in `AutoTarget.AmbushTickIdle` (`AutoTarget.cs:624`).** It reads the gate FIRST (`AmbushTacticsGranted`, `:713-717`); when un-granted the else-branch is **character-for-character the stock ambush idle** (`:674-688`). Only the gated branch calls `Stage3EvaluateSpring` (`:695`), a pure 1→5 precedence table (`AmbushTactics.EvaluateSpring`) fed by a cadence-gated kill-zone scan (`RecomputeAmbushScore`, `:751`, heavy work only every `AmbushScoreCadence` ticks).
  - **SPRUNG (`ambushTriggered`) is a TERMINAL latch, cleared only by `ResetAmbushState` (stance reset, `:912-917`).** The gated no-target branch clears only the tracking counters (`ResetStage3Tracking`) and deliberately leaves `ambushTriggered` set (`:653-659`) — the ungated stock branch clears it (`:661`). This "sprung stays sprung" rule is what gives a bot consumer a deterministic outcome (OBS-2): a fired unit that gets re-issued away and later re-idles does not re-arm and latch-churn. `AmbushSprung => ambushTriggered` (`:326`) is the read-only view consumers poll.
  - **The worthwhile score splits THREAT from VALUE, which is the whole reason a reinforcement lane is ambushable.** `ContactScore = wThreat·threat + wValue·value` (`:788`): threat is credited only to armed contacts (`AttackBaseInfo`, shaped base+HP/divisor+Cost/divisor, `AmbushThreatValue :836-846`); value is every contact's `ValuedInfo.Cost` (`AmbushCellValue :850-853`). An undefended supply truck reads threat 0 but value > 0, so it still saturates the spring trigger — a value-blind danger field (weapon-throughput only) would score it ~0 and let the juiciest target drive past.
  - **No velocity API — trigger-3 exit prediction comes from range SAMPLES.** `RadialSpeedPerTick = (curr−prev)/interval` sampled each cadence tick (`:811`), keyed on the best target's `ActorID` so a target swap resets the trend (`:806-820`); the prediction is `curr + radial·K > maxRange`.
  - **Determinism:** the kill-zone `FindActorsInCircle` is fog-filtered (`CanBeViewedByPlayer`, `:782`) and `OrderBy(ActorID)` before any nearest/best pick (`:783`); the new per-unit tracking fields are deliberately **NOT `[Sync]`** (like `ambushTriggered`/`PredictedStance`) — they evolve by pure integer/bool math over already-synced state with zero RNG, so they stay in lockstep across clients without contributing to the sync hash.
- **Stage 4 — `LaneAmbushBotModule` is the `@experimental` bot consumer** (`Traits/BotModules/LaneAmbushBotModule.cs`, an `IBotTick` player trait shaped like `PoiGarrisonBotModule`). It posts a small pool (`MaxAmbushes` × `UnitsPerAmbush`, defaults 2×2) of eligible units onto the corridor between our beachhead and an enemy anchor, and grants each the gate at runtime.
  - **The gate is granted through the shipped seam — no new grant wiring.** `EnsureGatedAmbusher` (`:440-464`) finds the unit's `ExternalCondition` whose `Info.Condition` matches `AmbushTacticsCondition` and calls `ec.GrantCondition(unit, this)` (permanent token, revoked with `TryRevokeCondition` on release, `:452/:474`). Stage 4 is the first thing that actually fires the seam shipped inert in Stages 1–2.
  - **OBS-2 — every posted unit is committed to the shared `PoiGoalGuard` ledger (`"ambush:<anchorId>"`, `:416`)** so the offense FSM treats it as taken and its ~75-tick re-issue never stomps the posting. The module polls `AutoTarget.AmbushSprung` each re-eval and **releases** a fired unit (revoke gate + `SetUnitStance FireAtWill`, which runs `ResetAmbushState` and clears the latch, + drop the ledger commit) so offense reclaims a fresh, un-latched unit (`PruneLanes :398-404`, `ReleaseUnit :469-488`).
  - **The lane is fog-legal.** The friendly anchor is `PoiMap.OwnSupplyRoute(player)` (public seam over `FindOwnSupplyRoute`, `PoiMap.cs:516-520`); enemy anchors are `PoiMap.GetOffensiveTargets(player, suppressOmniscientThreat: true)` (`:297`) filtered to `Pressure` (enemy SR) then `Attack`. SR positions are public map facts (`PoiMap.Discover` scans `world.Actors` for the SR type regardless of fog, `:203-227`) and `suppressOmniscientThreat: true` keeps the module off the omniscient `InfluenceMap` threat grid (mirrors the offense module — see [influence-stack.md §Stage F](influence-stack.md)). The post sits at `PostFractionPct`% (default 40 ⇒ our side of the midline) along the line, integer `WPos` interpolation in the pure `AmbushLaneMath` helper (`:589-613`, NUnit-pinned).
  - **Byte-identity is by ABSENCE — there is NO `@stable` twin.** The module is `RequiresCondition: enable-ai-experimental` with no `@stable` copy, so `@stable`/Normal/Rush/Turtle/humans never instantiate it, never commit to a ledger, never grant the gate. It draws zero RNG (fixed initial countdown in `TraitEnabled :157-163`, not a `LocalRandom` draw; every actor iteration `OrderBy(ActorID)`). `TraitDisabled` hands every posted unit back (`RetireAll` + a belt-and-suspenders grant sweep, `:184-198`) so a disabled module leaves zero granted tokens / ledger commits behind.

### Bot decisions ARE seed-reproducible (since main @ 2d3c8fe0)

`World.cs:213` seeds `SharedRandom` from the lobby `RandomSeed` (deterministic, network-synced); `World.cs:214` now also seeds `LocalRandom` — from that same `RandomSeed` via a fixed decorrelating transform `(int)(RandomSeed*6364136223846793005 + 1442695040888963407)`, guarded on `RandomSeed != 0` so normal gameplay (seed = `DateTime.Now`) still varies per launch. The bot modules make their *decisions* off `world.LocalRandom` (e.g. `UnitBuilderBotModule` picking which unit to call in; squad / layered-defence / support-power scan timing and target choice), so before this fix `LocalRandom` was unseeded (`new MersenneTwister()` → `Environment.TickCount`) and two same-`Test.RandomSeed` runs diverged within ~125 ticks. **Now a fixed seed is a *reproduction*:** verified byte-identical verdicts (and tick-by-tick score logs) across two seed-1017 hidden Mode-B matches, with a different-seed negative control diverging as expected (`WORKSPACE/ai-bench/runs/260720_seeded_determinism_verify.md`). The derived seed is decorrelated from `SharedRandom`'s combat rolls so the two MT streams stay independent. The verdict JSON records the seed (`verdict_version` 5). Note: OpenRA's off-thread (async) pathfinding did **not** need any extra work for this — it applies its results deterministically on the sim thread even with WW3MOD's modified movement, so seeding the single unseeded `LocalRandom` was sufficient for full byte-identical replay. Aggregate-over-N benchmarking is still the right way to *evaluate a code change* (one seed is one battlefield); what seeding buys is a *stable* mean over a fixed seed-set. Note: `LocalRandom` is "local" (non-synced) by OpenRA design — this makes a single-client benchmark reproducible, not a multiplayer match synced. **Do not "fix" this by routing bot decisions through `SharedRandom`.** `LocalRandom` is excluded from the sync hash — `World.SyncHash()` (`World.cs:540-554`) folds only Actors, `ISync` fields, synced effects, and `SharedRandom.Last` — so reseeding it cannot affect multiplayer sync, and bots emit their choices as *orders* (which synchronize peers/replays), not as RNG draws. Moving bot draws into `SharedRandom` would instead shift every subsequent combat roll and break the frozen `@stable` A/B baseline's RNG-stream byte-identity (the baseline was recorded *on* the seeded-`LocalRandom` build `2d3c8fe0`, so the decorrelated-seed fix is already baked in). Re-investigated and closed HOLD 2026-08-03: the safe fix is already in `main`, so re-implementing is a no-op.

### `RenderPlayer` is render-side only

`world.RenderPlayer` never touches the sim or the sync hash. `FogObscures`/`ShroudObscures` all short-circuit to `false` when `RenderPlayer == null` (`World.cs:109-114`), no player's `MapLayers` is mutated, and the sync hash reads `p.UnlockedRenderPlayer`, not `world.RenderPlayer` (`World.cs:543-547`). So switching a client to world-view (null RenderPlayer) leaves AI perception and the test verdict byte-identical — unlike the `DeveloperMode` "disable shroud" cheat, which does a **synced** `MapLayers.ExploreAll()` + `FogDisabled = true` per-player and thus changes targeting and the sync hash. Two consequences for tooling:

- **A World-actor render overlay must fall back to `LocalPlayer`** — in the autotest harness `world.RenderPlayer` is **null** even though the World-actor's `RenderAnnotations` is still called, so `var viewer = world.RenderPlayer ?? world.LocalPlayer;` is the correct local-client identity (still per-player-legal — reads only the viewer's own layer).
- `ShroudRenderer.UpdateShroud` only clears drawn shroud sprites when a render player is active; flipping `RenderPlayer` to null on a *live* client (world-view / `DevCinematicView`) must still clear each dirty cell's sprites or the map stays black despite uniform visibility.

## Widget / chrome authoring gotchas

Engine widget behaviors that fail **silently** — each cost real debugging time in the lobby work:

- **`ImageWidget` draws sprites at native size.** `Width`/`Height` are layout-only; `Draw()` calls `WidgetUtils.DrawSprite(sprite, RenderOrigin)` and ignores widget bounds (`ImageWidget.cs:78-91`). To scale a sprite into its bounds use the opt-in `ScaleToBounds: True` (uniform, centered). **When you add a field to a widget, mirror it in the copy-constructor** (`ImageWidget.cs:61`) — template clones run through the copy-ctor and silently drop any field you forgot.
- **`ButtonWidget` renders nothing for a missing chrome variant.** A highlighted button looks up `<Background>-highlighted` (`ButtonWidget.cs:320`), plus `-hover`/`-pressed`/`-disabled` suffixes; if that collection is absent, `WidgetUtils.DrawPanel` early-returns with no error and the button draws with no fill. Any custom `Background:` needs the full variant set.
- **Hidden widgets keep keyboard focus.** `Widget.IsVisible` is `() => Visible` (`Widget.cs:231`) — it checks the widget's OWN `Visible` flag, not its ancestors'. A focused `TextField` whose parent tab is hidden still looks visible to the focus system, so it keeps eating key presses (and Enter can fire its `onSelect`). Any tab-switch that hides a focused widget must hand focus off explicitly.
