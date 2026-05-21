# Changelog

All notable changes to WW3MOD, the total-conversion mod of OpenRA into a modern World War 3 RTS.

Forked from OpenRA `release-20230225` (February 2023). This document covers WW3MOD's divergence from that base — roughly 1,500 commits and a near-complete rewrite of the gameplay model.

The mod is currently public alpha; no stable releases have been cut, so dates are used as section anchors rather than version numbers.

---

## Identity (vs. Red Alert)

WW3MOD shares OpenRA's engine but almost none of Red Alert's gameplay:

- **No factories.** There is no Construction Yard, Barracks, War Factory, or Naval Yard. Each player starts with one indestructible **Supply Route** at their map edge; units are called in as reinforcements and march in from the map edge.
- **No tech tree.** Tech levels are time-gated; any unit your tech allows can be called immediately.
- **Two factions** — NATO/America and BRICS/Russia (Ukraine planned).
- **Modern doctrine** — suppression, garrisons, helicopter crews, vehicle crews, ballistic missiles, stances, cohesion.

---

## Core systems

### Supply Route reinforcement model
- Replaced Red Alert factories with a single per-player **Supply Route** building. Units spawn from the map edge nearest the SR and travel to its rally point.
- `ProductionFromMapEdge` trait for off-map reinforcement; `Production@Local` for buildings/defenses that spawn at the SR.
- "Buying" a unit allocates from a budget; **rotating units out** sends them home for a refund (`OrderResupplyHotkey`).
- Supply Routes are **indestructible**; defeat requires capture or contestation. Defeated player's SR turns neutral and re-capturable instead of dying (later: full contestation system).
- Ctrl/Alt modifier on the build button queues infinite production; right-click cancels a category.
- Multiple production categories share a single queue tick and unified economy display.

### Supply Route contestation
- Replaced classic capture with a **graduated contestation system**: bar fills based on hostile presence inside range, multi-phase coloring, defeat triggered when last team SR falls.
- `SupplyRouteContestation` world trait; range circle shown on selection, recolored white.

### Garrison system
- Buildings can be entered, soldiers fire from directional **ports** (NW/NE/SE/SW × 2 = 8 ports on civilian buildings).
- Phases: damage protection by garrison count, dynamic ownership, port targetability, suppression integration, shelter/port dual deployment.
- Per-soldier health/ammo pips rendered at the building; live ammo column visible.
- Building indestructibility while garrisoned; switches to "rubble" 1HP-evacuation state instead of dying.
- Vehicle-style health pips; hysteresis prevents port↔shelter flapping under fire.
- Civilian buildings, GTWR, PBOX, HBOX all unified under one garrison model.

### Vehicle crew system
- Three-phase rollout:
  1. Crew (Driver/Gunner/Commander) eject on critical damage.
  2. Crew can re-enter repaired vehicles.
  3. Commander substitution and full evacuation.
- Crew use real soldier sprites — hatch-emerge, walk away, go prone when wounded.
- Cookoff = FireDeath; total vehicle loss possible; vehicle fire overlay during burn.
- Crew inherit the wreck's burn intensity at the moment they emerge.
- Crew capture: walk your own crew into an empty enemy vehicle to take ownership.

### Helicopter crew & emergency landing
- Helicopters carry crews (pilot, copilot, gunner) with per-class survival rates.
- Heavy damage triggers **autorotation** — controlled descent with steering, flare, and rotor wind-down.
- Critical damage causes uncontrolled crashes; safe landings eject crew and turn the heli neutral for capture.
- Capture downed helicopters by walking your own pilot in.
- Helicopters fall straight down without spinning on instant kill; husks burn and decay.
- Crashed-heli RotateToEdge prevents map-edge cheese on evacuation.

### Suppression
- **10-tier infantry / 5-tier vehicle** suppression with decay over time.
- Suppressed infantry go prone past a threshold; vehicles lose turret traverse and accuracy.
- Per-soldier suppression hysteresis (later) stops garrison port↔shelter flapping.
- AT weapons can't fire while suppressed; AutoTarget breaks off suppressed targets.
- Suppression pip shown on regular infantry, not just crew.

### Stance system (3 fire × 3 engagement)
- Consolidated from a 5+4 mess into **3+3**:
  - Fire: HoldFire / ReturnFire / FireAtWill
  - Engagement: Hunt / Balanced / Defensive
- Cohesion: Tight / Loose / Spread distribution on group moves.
- Resupply Behavior: Auto / Hold / Evacuate (CargoSupply units inherit).
- Per-unit defaults persist via `UnitDefaultsManager`.
- Click-modifier meta-system: Ctrl/Alt/Shift modify orders against stances.
- Integrated with `SmartMove`, `AttackMove`, garrison fire discipline.

### Three-mode movement
- **Move** — smart self-defense only.
- **Attack-Move** — fire at everything along the path.
- **Force-Move** — pure travel, never fire.
- `SmartMove`: infantry pauses to fire at in-range targets while moving; mid-cell redirect for instant reaction to new orders.
- Vehicle reverse: short-distance backwards movement preserves facing; 120° cone; disabled on force-move.

### Cohesion system
- Click-anchored intent classifier replaces per-slot attractor.
- Cover-aware slot bidder via `IModifyGroupOrder` dispatch.
- Patrol system: waypoint queuing with bounce/circular looping.
- Group Scatter hotkey (Shift+G): distributes waypoints among selected units; handles `SmartMoveActivity`-wrapped, `AttackMove`, and `FlyAttack`-via-`IAttackActivity` orders.

### Cargo / supply economy
- `SupplyProvider` / `SupplyValue` system: numeric supply weight, refunded on sell/rotate.
- `TRUK` is dedicated supply truck (no passenger cargo). Drops `SUPPLYCACHE` crate; right-click LC delivers, default deploys.
- Cargo panel: individual passenger eject from transports.
- Waypoint-based selective unload; pre-queued ejection rally points.
- Logistics Center supply economy: P1 (deduct on sell), P2 (auto-refill empty trucks via Restock), per-batch SupplyValue.
- Auto-Enter button (3-mode): smart transport loading from any selection.

### Custom shadow / LOS system
- `ShadowLayer` precomputed LOS cache (`shadows.bin`) — per-map binary file.
- Dynamic recomputation amortized across ticks; building self-shadow exclusion; staggered actor-side recomputes.
- `--regen-shadows` utility command; `Map.cs` defers shadow fallback until after PostInit.
- Buildings no longer block LOS at runtime; shadow precompute handles all blocking.
- Off-by-one in `CellLayer.IsValidCoordinate` fixed — eliminated vision-through-trees at map edge.

### Counter-battery radar
- Separate detection layer for MSAR; range-aware coverage cleared on source death.
- Radar pings for detection events.

### Ballistic missile rework
- Multi-pass tuning over months:
  - Sine→parabola arc, physics-based acceleration, launcher erection animation.
  - `FacingLockCondition` keeps body locked during launch sequence.
  - Direct visual offset replaces pivot-based erection.
  - Analytical arc derivative for visual pitch; rendering above bridges.
  - Terminal-phase speed boost.
- Iskander and HIMARS as launchers; missiles commit to full salvo even if target dies.
- Beacon auto-removes when aircraft arrives or is destroyed.

### Nuclear weapons
- Realistic nuclear airburst: `ShockwaveDamageWarhead` + phased detonation.
- Sustained thermal pulses replace discrete pulses; staggered fire ignition over 2 seconds.
- Graduated fire zones replace instant mass tree husking.
- Feathered shockwave ring visual; subtle/stacking fire; progressive shake decay.
- Ground zero: electric death animation for vaporized infantry.

### Detection & cloaking
- Per-class detection circles; airborne adjustments.
- `Detectable` tick fix; pip rendering.
- Infantry cloaking removed (perf); cloak fixes elsewhere.
- Drones gain lost-connection condition when out of range; visuals dim.

### Production from map edge
- Round-robin fast spawn distributed across 5 cells (down from a single cell with 25-tick wait).
- `SpawnArea` actor controls spawn edge; bounded to 3 cells max with center preference.
- Rally point waypoints support per-waypoint order types and modifier keys.
- Aircraft evacuate via `FlyOffMap` and sell on edge arrival.

### Stances on autotarget
- Categorical priority: Heavy always beats Infantry regardless of range.
- AutoTarget breaks off when target hits critical damage.
- AlignBodyToTarget: units keep turning toward target while firing.
- Operator retargets at Critical damage; missile retargeting during flight.

### Custom traits (selected)
- `CargoSupply` (later replaced) / `SupplyProvider` / `DropsSupplyCache` / `QuickRearm`
- `HealerAutoTarget` + `HealerClaimLayer` — medics/engineers coordinate without doubling up.
- `GarrisonManager` + `WithGarrisonDecoration` + `GarrisonProtection`
- `HuskDecay` — gradual husk sink and fade.
- `VehicleCrew` + `HelicopterEmergencyLanding`
- `SmartMoveActivity` + `CohesionMoveModifier` + `PatrolSystem`
- `BotBlackboard` + `ThreatMapManager` + `InfluenceMap` + `FrontlineOverlay`
- `Passable` (replaces `Crushable`) — vehicles pass infantry, mines, fields, etc.
- `Locomotor` road speed bonus (50%), terrain-aware speeds.
- `TargetDamage` directional armor (penetration, thickness, top/bottom/side).
- `RangeDamageMultiplier` for falloff curves.
- `AmmoPool` + `LifetimeAmmoCap` (MLRS evacuates when empty).
- `ResetBurst` / `LockAimPerBurst` for true multi-shot bursts.
- `TerrainHeightAware` for ground-attack missiles.

---

## AI overhaul

WW3MOD ships a custom `ModularBot` ecosystem on top of OpenRA's `HackyAI`. Multiple stages over 2026:

- **Tier 1**: AI uses tech; Hunt stance for attack squads; ammo awareness; don't attack buildings being captured by own engineers.
- **Tier 2**: Rush AI and Turtle AI personality variants.
- **Tier 3**:
  - `ThreatMapManager` world trait for spatial reasoning.
  - `BotBlackboard` for inter-module coordination.
  - `ScoutBotModule` (map exploration, enemy intel).
  - `GarrisonBotModule` (defensive building garrisoning).
  - `SupplyFollowerBotModule` (field resupply logistics).
  - `AdaptiveProductionBotModule` (counter-composition builds).
  - Multi-axis attack capability.
  - Smart retreat + regroup-before-re-engage.
  - Helicopter AI: role-based squad module with hit-and-run.
- **v2 doctrine** (Stage A/B):
  - `InfluenceMap` + frontline derivation math.
  - `LayeredDefenceBotModule` (reserve-driven line filling, emergent flanking).
  - `MountedTransportBotModule` (infantry ferry by IFVs).
  - `CaptureCoordinatorBotModule` (income-weighted with escort and defense).
- **Tournament harness** (May 2026, Phases 1–17):
  - Engine plumbing, dual ModularBot, deterministic seeding, 8× speed multiplier.
  - Mirror-matching, comparator, faction field in verdict, batch loop tool.
  - Visible activation chat-lines for what each module is doing.
  - Findings: Russia 60% / America 40% in mirror batches.

---

## Maps & shellmaps

- **Tilesets refactor**: better forest support, smoothened shroud edges, fading SHP, airborne shadows.
- **Maps shipped**: River Zeta, Twin Rivers, Polar Disorder, Woodland Warfare, Nuclear Winter, Chernobyl WW3, Ukraine Frontline, arena-tank-duel.
- **River Zeta**: complete rewrite; west-side 3-player layout with island fix; field actor dedup (300 stacked); column-triplet tile fix; auto-position fields.
- **Map editor**: tab labels restored post-merge; `RadarWidget→MiniMap`; `MarkerLayerOverlay` re-added.
- **Shellmap system**:
  - Unified with normal maps — all maps are playable and shellmap-eligible via toggle.
  - Random rotation across 3 diverse battle shellmaps; dropdown selector; Replay last shellmap button.
  - MMB drag panning + scroll zoom; static center position; no edge scroll.
  - Pause/play icons; Nuke / Shellmap Replay buttons in top-right corner of main menu.
- **Decorations**: 4,971 decoration actors added to River Zeta shellmap.

---

## Scenario system

- Scenarios live inside maps and are selectable via lobby dropdown.
- Reusable Lua library + "Frontline" scenario example (River Zeta).
- `ShellmapScenario` support; filter shellmap from lobby; Lua safety guards.
- Scenario naming: `Scenario: Map Name` convention.
- Lua→C# `Nullable<T>` parameter conversion fixed.

---

## Tooling

### MCP Map Creation Server
- 17 tools for AI-assisted map creation: `create_map`, `read_map`, `list_maps`, `fill_terrain`, `paint_terrain`, `get_tileset_info`, `place_actors`, `remove_actors`, `list_actor_types`, `set_players`, `set_spawn_points`, `set_map_rules`, `write_lua_script`, `generate_preview`, `place_template`, `draw_road`, `auto_shore`.
- Terrain validation on actor placement; terrain grid in `read_map`.

### Combat balance simulator
- Tick-by-tick simulator for balance analysis (Phase 1: hardcoded stats; later rebuilt as live-YAML dashboard).
- Models damage (penetration, directional armor, range falloff, AoE), weapon firing cycles, suppression, formations.

### Developer test harness (AUTOTEST)
- `Test.Mode=true` launch arg activates harness; otherwise zero engine impact.
- Lua API: `Test.Pass` / `Test.Fail` / `Test.GetTargetOrder` / `Test.Screenshot` / `Test.OpenSkirmishLobby` / `Test.OpenLobbyTab` / `Test.LaunchLobbyMap`.
- Tiers: auto-asserting Lua, batch runner, test discovery, RESTART (F4), windowed positioning, per-terminal saved position.
- Background-mode default, focus restore, mute by default.
- Scenarios in `tools/autotest/scenarios/`; `MapFolders` class `Unknown` keeps them out of in-game chooser.
- Screenshot recipe: in-test (Lua) and external (lobby/menu) modes; multimodal `Read` for semantic eval.

### Demo system
- Trigger-staged scenarios for the human to explore (no verdict, no autonomous loop).
- Lives alongside autotests; `run-demo.sh` runner.

### Build tooling
- `make.ps1` extra output; faster build speed; solution file fix.
- `engine/Directory.Build.targets`: atomic-replace DLLs on Unix so builds don't crash a running game.
- Server GC + Concurrent GC (later reverted; workstation default works better).
- Hidden `tools/autotest/scenarios/` registered in `mod.yaml`.
- `tools/git-hooks/pre-commit` enforces engine-code rules (no `Console.Write` in tick path).

### Dev helper
- `./ww3-dev.ps1` — build, run, test, pre-flight checks, debug log cleanup.
- `launch-game.sh` — PseudoFullscreen by default; auto-build before launch on Windows.

---

## UI & visuals

### Lobby redesign (May 2026, Phases 1–12)
- Tabs top, MATCH/Advanced layout, auto-roster, full-width Start.
- Common options grouped into Economy/Match/World.
- Accordion sections on Advanced tab; chips clickable (jump to option's panel).
- Named presets with save/load/rename; Last-game preset; Active Changes chips.
- Empty slots get inline quick-action buttons (flags for factions, glyphs for actions).
- 2×2 quadrant layout (Q4); map preview enlarged; inline map browser; Music tab over chat.
- V5 player rows: color · flag · spawn · name · ready.
- Steel/gray palette with bevel convention; section header dividers; flat row chrome.
- Hidden TEAM/HANDICAP widgets pushed off-screen (`X:-200`) until reintroduced.

### In-game UI
- Build menu: ratio cycles + two-number badge + primitive auto-stripe.
- Production tooltip: anchored to sidebar; auto-generated weapon block.
- Stance bars inline with command bar; segmented backgrounds with dividers.
- Info button replacing version label; full-width top info bar.
- Cohesion/Resupply Behavior stance bars; new command buttons.
- Range circles: 8 categories with consistent color/width; grouped rendering (dim interior, highlight outer envelope); 3× finer segments; boundary margin.
- Cargo pips, garrison pips (per-soldier health+ammo column).
- EVA indicator (pip-orange icon) on evacuating units; waypoint line.
- Show All Orders (hold Space): reveal all friendly unit orders/waypoints.
- Healing/repair flash on units.

### Visuals
- Pixel art scaling shader for world sprite rendering.
- Dim actor sprites based on fog-of-war visibility level.
- Fog overlay extended into beyond-map area; shroud gradient at map borders.
- Tree X-ray overlay: visible units never hide behind occluders; tree flip at visual middle.
- Z-order: universal west-on-top tiebreaker + `RenderSprites.XRenderOrder`; field ZOffset `-256→-8192`.
- Bullet trails; missile smoke trails; sustained thermal pulses on nukes.
- Cinematic map reveal cheat (`/cview`) — visual only.
- Cosmetic Reveal debug cheat (3-state OFF/ME/ALL).
- 3-state instant build / quick build (10× speed); save/load preset buttons.
- Loading screen: dark gray bar with WW3MOD title; themed hints.
- Top-right button bar: pause/play, info, shellmap, nuke buttons.
- Counter-battery radar trefoil glyph for nuke button.
- Helicopter husks fall straight down on instant kill.

### Hotkeys & settings
- Modern modifier keys: Ctrl/Alt/Shift consistently across orders; Alt delay fix; force-move cursor.
- Group Scatter hotkey (Shift+G); AutoRearm hotkey.
- Resupply (rotate-out) hotkey.
- Global ToggleMute hotkey (works regardless of active chrome window).
- Settings: disable infantry visual scaling; Backspace for ToLastEvent (Space repurposed).

---

## Balance

### Weapons & armor
- Armor penetration + thickness; directional armor (top/bottom/side); range damage multiplier; range-scaled inaccuracy.
- `TargetDamage` warhead variants for `RandomDamage` rolls.
- MLRS true bursts (Grad/M270/TOS) with `LockAimPerBurst`.
- Counter-battery radar coverage on artillery; Hellfire flat range; WGM tighter inaccuracy + lock-on aim.
- Tank/WGM accuracy pass; Mi-28 fires Ataka (SACLOS); Apache keeps Longbow.
- Sniper damage 350; littlebird minigun range 15→8c0 with inaccuracy 0c256→0c64.
- M109 light armor; Bradley/BMP/TOS/ATGM damage adjustments.
- Iskander/HIMARS rebalance with Logistics Center supply economy.

### Health & damage
- Doubled infantry & vehicle health; critical level set to 50%.
- Tank HP bump; default vehicle health 5000.
- Crew HP inherits default infantry (200).
- Crew survival per-class; critical damage full disable.
- C4 routes through damage pipeline.

### Economy
- Per-batch `SupplyValue`; `CreditValue` collapsed; tier values for all weapon classes.
- Tier values for infantry, crew, defenses, all faction vehicles, all faction aircraft.
- Adjusted Cash delivered on rotation to match handicap.
- Removed Power, War Crime Penalty, Repairable Building, Refinery, Spy — ~1500 lines of legacy RA dead code.
- IncomeModifiers lobby option.

### Disabled / removed units
- SHOK trooper (futuristic-tier cleanup).
- Tesla Trooper, Tesla Coil, Prism Tower (futuristic units).
- Airstrike support powers for v1.
- Spy / dog removed earlier.

---

## Bug fixes (selected)

A few hundred small fixes; only structural ones called out:

- **Helicopters**: velocity movement (precise stopping, landing, position alignment); landing orbiting (proportional braking, lateral drift kill); spawn/evacuate uses SpawnArea edge.
- **Vehicle reverse**: maintain facing through curved paths; bounce on forward↔reverse transition; sliding on curved paths.
- **Missile targeting**: missiles launching wrong direction (Bradley/BMP WGAT); aiming cruise altitude instead of target; orbiting target (turn rate boost in Hitting state); speed freeze (allowPassBy false-trigger); 20% minimum speed floor.
- **Pathfinder**: `HierarchicalPathFinder` crash on unreachable cells; HPF respects Passable, not just Crushes.
- **AI helicopters**: bootstrap capacity check + HPAD priority; `SkipRearmBuildingCheck` for reinforcement model.
- **AutoTarget**: drop `AttackBaseInfo` requirement so weaponless units (TRUK) host stance state; snapshot `ActiveAttackBases` in Created.
- **Sync**: float math + missing `[Sync]` attributes (multiplayer sync risks).
- **Crash fixes**: shellmap crash on switching maps; `ProduceActorPower` lowercase actor name; airstrike case-sensitivity; `WithCargoPipsDecoration` sequence-before-Image; `ChangeOwnerInPlace` for buildings; capture freeze on neutral structures; `KeyNotFoundException` on `GrantConditionOnPrerequisite` ownership change; color picker NRE in lobby; `Math.Clamp` in multi-axis squad split; SNOW tileset missing Tree type; `Passable` NRE on aircraft over passable actors.
- **Garrison**: stop cancelling in-flight enter orders on ownership flip; per-soldier suppression hysteresis; stable shelter pip order via ActorID sort; double-display prevention via invariant guards.
- **Vision**: vision-through-trees off-by-one in `CellLayer.IsValidCoordinate`; targeting through fog (defense-in-depth position-based check); `FrozenUnderFog.IsVisible` hardcoded true.
- **Captures**: TECN-only for neutrals; soldiers take enemy-owned by force; crew capture via `Passenger` targeter no longer blocks `CrewMember` order.

---

## Performance

- Cache trait lookups in `GarrisonManager` and `HealerAutoTarget`.
- Eliminate per-tick allocations in `Armament`, `AutoTarget`, `GarrisonManager`, `HealerAutoTarget`.
- Replace LINQ with loops in bot squad states; remove `ToList+OrderBy` in `AttackBase.ScanForNewTarget`.
- Remove redundant `.ToArray()` in Turn and Turreted modifier calls.
- Allocation pooling in vision update path.
- `AffectsMapLayer`: throttle vision recalcs during movement, stagger init by ActorID.
- Stagger periodic scans/decay across actors to eliminate sync spikes.
- Cap GL debug error cascade to prevent OOM crash.
- Amortize shadow recomputation across ticks; batch on player defeat.
- Garrison entry: skip expensive `World.Remove/Add` on ownership change.
- `PerfTickLogger`: tag long-tick entries with GC collection deltas.

---

## Engine upstream merges

- **`release-20230225` → `release-20250330`** (March 2026): 3226 files categorized into merge phases; restored WW3MOD files from main; resolved ~130 compilation errors down to 0 across `OpenRA.Game`, `OpenRA.Mods.Common`, `Cnc`, `D2k`.
- **Phase 2a**: Platforms.Default, glsl shaders, Tests (44 files).
- **C# language version**: 7.3 → 9 (matching upstream).
- **Subsequent fixes**: runtime errors, widget rename, expression parser whitespace, custom field restoration, fluent files, `LobbyOptionsLogic`, `DefaultScale` in `WorldViewportSizes`, scale parameter in `SpriteEffect`, `ChangeTick` sequence property, viewport zoom (4× max, 0.25× spectator), `Alt` modifier fix.

---

## Documentation

- **`CLAUDE.md`** — agent instructions (March 2026), enforce commit-after-every-response rule; rule against autonomous multi-test batch runs; PITFALL comment convention for recurring traps.
- **`DOCS/`** — system reference: architecture, AI strategy, supply route, economy, missiles, lobby redesign, capturing.
- **`WORKSPACE/`** — living state: `RELEASE_V1.md` (v1 tracker), `HOTBOARD.md` (in-flight), `BACKLOG.md`, `DISCOVERIES.md`.
- **Modes**: `RELEASE` (default, scope-locked v1) and `EXPERIMENTAL`.
- **Recipes**: `PLAN`, `PLAYTEST`, `TRIAGE`, `AUTOTEST`, `DEMO`, `REVIEW`, `FINALIZE`, `CONTEXT`, `BALANCE`, `TELEMETRY`, `SCREENSHOT`, `DOCUMENT`.
- Issue/PR templates, SECURITY.md, CREDITS.md, dependabot, CONTRIBUTING/CODE_OF_CONDUCT.
- README rewrite (May 2026); roadmap entry point via `WORKSPACE/RELEASE_V1.md`.

---

## Tests

Unit tests in `engine/OpenRA.Test/` (NUnit 3):

- `AmmoPoolTest.cs` — `GiveAmmo`/`TakeAmmo` clamping, initial ammo, `SupplyValue`, `FullReloadTicks` math.
- `SupplyProviderMathTest.cs` — distance-based delay formula, supply deduction, selection bar.
- `SuppressionMathTest.cs` — infantry/vehicle tier progressions, decay timing, caps, prone threshold.

Plus 40+ autotest scenarios across combat balance, garrison, supply economy, helicopter behavior, missile flight, ballistic arc, and AI/scenario gameplay.

---

## Internal

Dozens of internal refactors, error-fix sweeps, and YAML lint cleanups (1397 → 11 errors); silent-ignore detection after upstream merge; redundant `OrderBy`/`ToList`/`ToArray` removal; debug log spam strip-outs; unused-code/dead-YAML deletion.

---

*Generated from `git log release-20230225..HEAD` (~1538 commits, Feb 2023 – May 2026).*
