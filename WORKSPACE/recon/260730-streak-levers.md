# Recon: streak levers — transport / supply / purchasing / stages

**Researched against `main @ 5ff997e5`** (2026-07-30; note: local HEAD is ahead of origin/main by 1, tree clean apart from unrelated untracked files). READ-ONLY code reading; every claim cites file:line.

Purpose: map the existing seams for four upcoming `@experimental` bot-AI improvements so implementation briefs can be written precisely. This doc carries the detail; the turn message carries the condensed summary.

Scope note (the recurring trap): there are **no factories / tech tree**. Units are called in as reinforcements via the **Supply Route** (`ProductionFromMapEdge`) — they spawn at the map edge nearest the SR and walk/fly to the SR rally point. "Purchasing" = calling in from off-map reserves. See `DOCS/reference/game-model.md`, `supply-route.md`.

---

## Q1 — TRANSPORT SHUTTLE (`MountedTransportBotModule`)

**File:** `engine/OpenRA.Mods.Common/Traits/BotModules/MountedTransportBotModule.cs`
**YAML:** `ai.yaml:438` (`@poi`, `enable-ai-stable`, frozen) + `ai.yaml:461` (`@experimental`). Split from a former shared singleton; consumers resolve the enabled instance via `TraitsImplementing<>().FirstOrDefault(!IsTraitDisabled)` (both never enabled on one player). Config identical except the experimental-only `DeliverBeforeContact: true` (`:472`), `PreContactStagingPct: 50` (`:473`), `UnloadOnArrival: true` (`:476`).

### What it currently does
Per-carrier state machine `Loading → Delivering → Unloading → Returning` (`:93`), one `CarrierTask` per carrier (`:95`). `BotTick` (`:222`) every `ScanInterval` (50 ticks): find own SR (`FindOwnSupplyRoute`, `:194`), drop stale tasks, advance live tasks (`AdvanceTask`, `:252`), then `TryAssignNewTasks` (`:362`).

- **Carrier pool** (`:372`): owned, alive, of `CarrierTypes` (`bradley,bmp2,m113`), **has `Cargo`, is EMPTY, not already tasked**. Deliberately does NOT require `IsIdle` (PITFALL `:370` — re-adding `IsIdle` reintroduces the `carriers-candidate=0` bug; carriers auto-target distant scouts and are never idle). `Stop` order parks them for boarding.
- **Passenger pool** (`:415`): infantry of `PassengerTypes` **within `ReserveZoneRadiusCells` (14) of own SR** — this is the load-bearing gate. `MinPassengersPerLoad` 2, `MaxPassengersPerLoad` 5. `EnterTransport` orders sent (`:464`).
- **Drop-off** (`PickDropOffCell`, `:492`): the thinnest cell of OUR frontline — `influenceMap.GetFrontline(player)` then the cell of lowest `GetFriendlyInfluence` (`:497-533`). Enemy concentration is deliberately NOT considered (`:521`).
- **Unload** (`:298`): `@experimental` issues `"Unload"` (`UnloadOnArrival`); frozen issues the broken `"UnloadCargo"` no-op string on purpose (`@stable` byte-identity). Re-issues `Unload` if arrival cell was blocked (`:330`).
- **Return** (`:340`): back to the SR rally cell (`task.Return = srCell`), then task dropped.
- Also hosts the **TECN capture-ferry** directed path (`TryReserveCaptureFerry`, `:136`), called by `CaptureCoordinatorBotModule@experimental`.

### Why early-game infantry still WALKS to the front
The module is **enabled** (not disabled) — that is not the cause. Four real reasons, in order of impact:

1. **The pickup window is a 14-cell bubble around the SR, scanned only every 50 ticks** (`:414-420`). Infantry spawn at the map EDGE and walk toward the SR rally; `LayeredDefenceBotModule` / `PoiOffensiveBotModule` grab fresh production and send it forward *immediately*. A unit that transits the 14-cell reserve bubble between two 50-tick scans is never eligible → it walks the whole way. The bubble catches only units still loitering near the SR at scan time.
2. **No frontline ⇒ no destination (frozen side does nothing).** `PickDropOffCell` returns null when `influenceMap == null` OR `GetFrontline == null` — i.e. before first contact (`:494/:498`). The frozen `@poi` twin then **sits idle** (`DeliverBeforeContact` false). So pre-contact there is zero ferrying on stable; pure walking. `@experimental` falls back to `PreContactStagingCell` = a naive **50% lerp** from SR to the top PoiMap offensive target (`:539-553`) — better, but not "edge of battle outside enemy visual range."
3. **Carrier scarcity early.** Needs empty `bradley/bmp2/m113` present and untasked. Static call-in weights are mid-pack (bradley/bmp2 25, m113 15 — see Q3), and these carriers are the ONLY consumers of themselves (excluded from LayeredDefence). Few IFVs early ⇒ few/no ferries.
4. **Min-load gate** (`MinPassengersPerLoad` 2, `:452`): a lone fresh infantryman is never ferried.

### What's missing for the full loop the user wants
> fill → drive to edge of battle but OUTSIDE enemy visual range → unload → infantry take positions → transport returns for more (or retreats behind the line on defense)

- **"outside enemy visual range" drop:** absent. Drop-off is either the thinnest friendly frontline cell (can be a hot cell) or a blind 50% lerp. No standoff, no enemy-vision-aware or concealment-aware drop. Notably the module still reads the **OMNISCIENT** `influenceMap.GetFrontline / GetFriendlyInfluence` (`:497/:501`) — `influence-stack.md:101` lists `MountedTransportBotModule` (`GetFrontline :497`, `GetFriendlyInfluence :501`) as **deliberately NOT migrated** to the belief/danger fields. A fog-legal, AA/vision-aware drop would consume `DangerFieldLayer` / `BeliefStore` (already live for experimental bots) to pick a drop just outside believed enemy sight.
- **"infantry take positions":** no follow-on order after unload; passengers revert to whatever LayeredDefence/PoiOffensive next assigns.
- **"transport retreats behind the line on defense":** absent. `Returning` always goes to the SR rally; no defensive-posture variant that holds the carrier behind the engagement line.
- **Re-ferrying forward units:** impossible by design — the reserve-zone gate excludes anyone already forward.

**Cleanest seams:** `PickDropOffCell` (`:492`) for the standoff/vision-aware drop; the `Returning` case in `AdvanceTask` (`:340`) for the defensive-retreat variant; the passenger filter (`:415`) if the pickup window needs widening (e.g. catch units mid-walk along the SR→front lane).

---

## Q2 — SUPPLY LOGISTICS (`truk`)

**Owning module of `SupplyTruckTypes: truk` (`ai.yaml:344`):** `SupplyFollowerBotModule@supply` (`ai.yaml:342`, `enable-ai-any` — runs for BOTH bots). Field decl `SupplyFollowerBotModuleInfo.SupplyTruckTypes` (`SupplyFollowerBotModule.cs:25`).

### Do bots PURCHASE trucks?
Yes. Static composition `truk: 20` weight, `UnitLimits truk: 4` in both faction files (`ai-america.yaml:37/87`, `ai-russia.yaml:36/86`). BUT `@experimental` gates the call-in: `GateResupplyOnAmmoNeed: true` + `ResupplyUnitTypes: truk` (`ai-america.yaml:94`, `ai-russia.yaml:94`) → `UnitBuilderBotModule.BuildUnit` skips the truck unless `AnyFieldedUnitNeedsResupply()` (`UnitBuilderBotModule.cs:171/183` — some truck-rearmable fielded unit has ammo need ≥ `ResupplyNeedThreshold` 0.05). Stable/frozen build trucks on the raw weight schedule.

### Do they ROUTE trucks to units at the front?
Yes, via `SupplyFollowerBotModule.BotTick` (`:115`), every `ScanInterval` 150:
1. Eligible trucks (`:139`): owned `truk`, **not** `IsLowOnSupply` (below `RestockThreshold` or empty — those are left to `SupplyProvider`'s auto-restock to LC, `:346`), not claimed by another module.
2. Friendly clusters (`FindUnitClusters`, `:234`): groups of ≥ `MinNearbyFriendlies` (4) within 10 cells; each cluster carries an `AmmoNeed` sum.
3. Per truck (`:162`): pick closest cluster within `MaxFollowDistance` (35 cells) ordered by `AmmoNeed` then distance; `FindSafeFollowPosition` (`:281`) picks the lowest-threat cell within ±3 of the cluster (via `ThreatMapManager`); `Move` the truck there.
4. `@experimental` adds **Stage-E `DangerFieldRouting`** (`ai.yaml:353`): a two-leg detour via safer depth (`GroundDangerNav.DetourWaypoint`, `:197`), gated on `InfluenceStack.Participates` (`:109`) so other profiles stay byte-identical. Resupply itself is `SupplyProvider`'s passive aura — the module only positions the truck.

### What's missing for the user's ask
> multiple trucks active, assigned to different front sectors, drive-past rearming / crate-drops at critical points, evacuate after

- **Sector assignment / de-dup:** absent. The per-truck loop (`:162-231`) lets every truck independently pick its own best cluster with **no "cluster already served" exclusion** — two trucks can pile onto the same cluster. Multi-sector coverage is accidental, not enforced. Seam: add a served-cluster set / round-robin in the assignment loop.
- **Drive-past rearm / crate-drops:** absent. The module issues only `Move` near the cluster and leans on the passive `SupplyProvider` aura. No explicit resupply order, no drive-*through*-a-lane, no crate-drop. (Crate-drop rearm is being fixed in parallel — it is **not invoked here at all**; a future arrival-order at `:180`/`FindSafeFollowPosition` would be the hook.)
- **Evacuate after:** absent. Post-positioning the truck just re-follows next scan, or when it runs low is *released* to `SupplyProvider` restock (`IsLowOnSupply`, `:125/:346`). No deliberate "pull back to safety after dropping supplies."
- **Small-army blind spot:** `MinNearbyFriendlies` 4 means early tiny forces (<4 clustered) get **no** truck follow.

**Cleanest seams:** `FindUnitClusters` (`:234`) + the assignment loop (`:162`) for sector split/dedup; `FindSafeFollowPosition` / the arrival branch (`:180`) for crate-drop + evac.

---

## Q3 — PURCHASING / UNIT-MIX INTELLIGENCE

**Two independent sources** (confirmed by `architecture.md:326-342`). Only these traits implement `IBotRequestUnitProduction` sinks/callers in WW3MOD: `UnitBuilderBotModule` (the sink), `AdaptiveProductionBotModule`, `CaptureCoordinatorBotModule` (TecnFloor). McvManager/Harvester are unused.

### 1. Static composition — `UnitBuilderBotModule`
`UnitsToBuild` weights are **share ceilings, not priorities** (`ChooseUnitToBuild`, `UnitBuilderBotModule.cs:275-293`): shuffle the dict, return first type whose `count/total < weight/100`. Any weight ≥100 never binds ⇒ merely "always eligible," then picked **uniformly by shuffle** (weight 500 vs 120 give identical odds). **Critically, while `idleBaseUnits < IdleBaseUnitsMaximum` (12) the module ignores weights entirely and builds a UNIFORM-RANDOM buildable** (`ChooseRandomUnitToBuild`, `:265-273`, called at `:126`), discarding non-`UnitsToBuild` picks (`:156`). So the opening is essentially random draws across the whole buildable roster.

Faction rosters (`ai-america.yaml` / `ai-russia.yaml`): infantry-dominant (e3 120, ar 100, tl 80, e2 60), tecn 500 (always eligible, capped limit 3), support infantry (at 50, mt/medi 40, aa 30, sn 25, e6 20), vehicles mid (btr/humvee 30, bmp2/bradley 25, t90/abrams 20), **AA low (tunguska/strykershorad 10, limit 2)**, artillery 15, `truk` 20.

### 2. Reactive counter — `AdaptiveProductionBotModule` (`@experimental`, `EvaluationInterval` 300)
Reads blackboard intel `enemy-vehicles/infantry/buildings-sighted` (`:112-114`) **posted by `ScoutBotModule`** scanning around the scout out near the **enemy base** (`ScoutBotModule.cs:243-283`) PLUS its own fog-legal global `ScanEnemyComposition` (all visible enemies, `:200-234`). Maps category → pool: vehicles→`AntiVehicleUnits`, infantry→`AntiInfantryUnits`, air→`AntiAirUnits` (`:131-151`). **Gated `MinEnemySightings` 3** (vehicles+infantry total, `:117`). Pushes ≤2 requests/cycle, each a **random draw** from the counter pool (`:179`), through the demand queue (which skips weights/limits/delays, `UnitBuilderBotModule.cs:130-165`).

### Opening ~5 minutes, and reaction to enemy comp
- First ~12 idle units: uniform-random roster draws (includes AA/artillery, hence "SHORAD at start" — this is the STATIC path, not over-reaction; `architecture.md:342`).
- After idle cap: infantry-heavy share-ceiling composition + tecn spam toward its cap.
- Counter-purchasing exists ONLY through AdaptiveProduction, coarse (category → random pool member), and only after 3+ sightings via a scout near the enemy base or globally-visible enemies. No specific matchup logic.

### ADDENDUM — SR rushed by 2 tanks → bot bought Tunguska + random infantry
- **(a) Does any module observe nearby/incoming enemy composition at the SR?** **NO.** `AdaptiveProduction.ScanEnemyComposition` scans ALL visible enemies globally, with **no SR-proximity weighting** (`:200`); the scout intel it also reads describes the ENEMY base area, not our SR (`ScoutBotModule.cs:243`). **Neither reads the belief/danger fields for purchasing.** The influence stack (`DangerFieldLayer` / `BeliefStore`) is consumed only by offense/heli/truck ROUTING and capture/garrison SCORING (`influence-stack.md` consumer map) — **never by the call-in decision**. There is no SR-defense purchase observer anywhere.
- **(b) Any threat→unit-class counter mapping?** Only AdaptiveProduction's category map, and **it was blocked in this case**: 2 tanks = 2 sightings < `MinEnemySightings` 3 → early return, does nothing (`:117`). Even had it fired, it draws a RANDOM member of `AntiVehicleUnits` (`at`/`t90`/`bmp2`), not a deliberate AT pick. The observed **Tunguska = static composition** (weight 10 building toward limit 2); the **"random infantry" = the uniform-random opening**. Purchase logic literally never saw the tank threat. (Note: BMP-2 carries the AT missile the user wanted, but nothing maps "armor at SR" → BMP/AT.)
- **(c) Cleanest seam to insert "imminent ground-armor threat at SR → prioritize AT call-ins":** `AdaptiveProductionBotModule` is the natural home — it already owns the threat→counter map and the `IBotRequestUnitProduction` plumbing. Add an SR-proximity read: sample `DangerFieldLayer.GroundDanger` (or `BeliefStore.Contacts` filtered to armed ground contacts) around `FindOwnSupplyRoute`, and when armor danger is present, **bypass `MinEnemySightings`** and push high-priority `AntiVehicle` (AT/BMP) requests. This mirrors the existing `CaptureCoordinator`/`PoiGarrison` believed-danger read pattern and reuses the demand queue (which already skips weights/limits). The fog-legal SR-threat read is already available for experimental bots (`InfluenceStack.Participates`). A dedicated SR-defense purchase module is the heavier alternative; AdaptiveProduction reuse is cleaner.

---

## Q4 — GAME-STAGE AWARENESS

**Existing phase notion: exactly ONE, binary, single-consumer.** `BotEarlyGameMath.EarlyGamePhase.IsEarly(worldTick, enabled, durationTicks)` = `enabled && worldTick < durationTicks` (`BotEarlyGameMath.cs:96-104`). Consumed only by `PoiOffensiveBotModule` `EarlyGameSpread` (`ai.yaml:167`, `EarlyGameDurationTicks` 4500 ≈ 3 min) to use smaller axis packets (`EarlyUnitsPerAxis` 3 vs 8) while young. That is the entire stage system.

No mid/late phase, no income-threshold posture switch, no stage-keyed aggression anywhere else. Various `ScanInterval` / `CommitmentTicks` / `EvaluationInterval` are fixed cadences, not phases. `AdaptiveProduction`'s interval is constant.

### Cleanest seams to introduce stage-gated behavior
- **Pattern to copy:** `BotEarlyGameMath` — a pure, engine-free, zero-RNG, NUnit-pinned static classifier (`BotEarlyGameMathTest`). Extend `EarlyGamePhase` into an enum classifier `{Opening, Mid, Late}` keyed on `worldTick` and/or income.
- **Best home for a SHARED phase service:** a World-actor or Player trait mirroring `InfluenceStack` — a single place that answers `Phase(player)`, so all modules narrow to one definition (exactly how `InfluenceStack.Participates` centralizes gating, `influence-stack.md:11-16`). Modules already grab world/player traits in their `Initialize()`; a `GamePhase` trait would slot in identically. Candidate consumers: `PoiOffensiveBotModule` (axis size / aggression), `AdaptiveProductionBotModule` (opening counter-weighting; ties into Q3), `MountedTransportBotModule` (pre-contact staging aggressiveness), `SupplyFollowerBotModule` (when to commit trucks forward).
- **Income signal:** available on the player actor's resources trait (`player.PlayerActor`) — the standoff between "young by tick" and "rich by income" gives a robust phase estimate.
- **Cheapest incremental step:** every module already holds `world.WorldTick` and its own Info, so a per-module `IsEarly`-style gate reusing `EarlyGamePhase` is a low-risk first move; promote to a shared trait once ≥2 modules need it. **Invariant to honor:** any new behavioral field on a shared trait class must default to the frozen behavior and be opted in per-profile via YAML (`architecture.md:344`), and any always-on world layer must draw zero `SharedRandom` (`influence-stack.md:94`).

---

## File map (for the briefs)
| Q | Primary file(s) | Key lines |
|---|---|---|
| 1 | `MountedTransportBotModule.cs` | state machine `:93`; assign `:362`; passenger gate `:415`; drop-off `:492`; pre-contact `:539` |
| 2 | `SupplyFollowerBotModule.cs` | tick `:115`; clusters `:234`; assignment loop `:162`; safe-follow `:281`; Stage-E `:188` |
| 3 | `UnitBuilderBotModule.cs` (static), `AdaptiveProductionBotModule.cs` (reactive), `ScoutBotModule.cs:243` (intel), `ai-america.yaml`/`ai-russia.yaml` (rosters) | share-ceiling `:275`; idle-random `:265`; counter map `:131-151`; gate `:117` |
| 4 | `BotEarlyGameMath.cs` | `EarlyGamePhase :96`; consumer `ai.yaml:167` |
