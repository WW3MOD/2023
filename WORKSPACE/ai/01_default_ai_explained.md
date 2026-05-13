# Default AI — How the engine's bot thinks

> Reference doc. Before we design a replacement, we need a precise model of what we're replacing. This document describes the **stock OpenRA AI** as it exists in the engine — the bot you get when you load `ModularBot` with the upstream modules attached. It deliberately ignores the WW3MOD-specific modules we recently added (`LayeredDefenceBotModule`, `CaptureCoordinatorBotModule`, `MountedTransportBotModule`, `AdaptiveProductionBotModule`, `SupplyFollowerBotModule`); those are surveyed separately later when we map the migration.
>
> Goal: identify every place where decisions are made, every hook where new information can be injected, and every implicit assumption that breaks under WW3MOD's reinforcement model. Treat this as the **map of the existing machinery** before we start drawing the replacement.

---

## 1. The big picture, in one paragraph

The engine's AI is not a single brain. It's a **bag of independent modules** that each tick on their own countdown, each owns its own slice of decision-making, and each shouts orders into a shared queue without consulting the others. There is no top-level planner — the "behavior" you observe is the emergent overlap of ten or so modules running in loose parallel. The single shared coordination point is a `BotBlackboard` that holds unit claims and intel keys, but it is partially-implemented and only four modules speak to it. Most modules use `Actor.IsIdle` as their sole "is this unit free?" check, which is the largest architectural footgun in the system. There is no concept of a goal, a plan, or a commitment that outlives a single tick.

If you want to know "what does the AI do?", the honest answer is: **it doesn't do anything as a whole — it runs a dozen reflex loops in parallel and the player sees the average**.

---

## 2. The driver — `ModularBot`

File: `engine/OpenRA.Mods.Common/Traits/Player/ModularBot.cs`

`ModularBot` is a thin shell. On player activation it snapshots two trait arrays from the player actor:

- `IBotTick[]` — modules that get a tick every world frame
- `IBotRespondToAttack[]` — modules that react when one of the bot's actors is damaged

Then on every world tick it does exactly three things, in this order:

1. **Tick every enabled module.** `foreach (var t in tickModules) if (t.IsTraitEnabled()) t.BotTick(this);` — no priorities, no sequencing logic, no "did module A produce output that module B should read?". The iteration order is **the order the traits were constructed**, which in turn is **YAML declaration order** on the player actor (modulo `NotBefore<>`/`Requires<>` constraints, of which there are none meaningful here). On `mods/ww3mod/rules/ai/ai.yaml` the order on each `ModularBot@<difficulty>` block is roughly: BuildingRepair → Scout → Garrison → SupplyFollower → AdaptiveProduction → LayeredDefence → MountedTransport → CaptureManager → CaptureCoordinator → BaseBuilder → UnitBuilder → SquadManager → HelicopterSquad.
2. **Drain part of the order queue.** Modules don't issue orders directly — they call `bot.QueueOrder(order)`, which enqueues. Each tick, `ModularBot` dequeues ≥ ⌈queue / MinOrderQuotientPerTick⌉ orders (default `MinOrderQuotientPerTick = 5`, so ~20% of pending per tick) and passes them to `world.IssueOrder`. This rate-limit is the only thing approximating "thinking time"; it prevents a giant burst of orders from being issued in one tick.
3. **Forward damage events.** A separate `INotifyDamage.Damaged` handler iterates `attackResponseModules` and lets each one observe the attack. This runs out-of-band from the per-tick loop.

There is no main-loop logic in `ModularBot` itself. Everything interesting happens inside the modules.

**Hook point 1**: any `IBotTick` we register on the player actor will be ticked. Position it first in YAML declaration order and it runs first in the frame. Position it last and it runs after every other module has queued its orders — useful for "veto / rewrite" passes.

**Hook point 2**: any `IBotRespondToAttack` we register receives damage events out-of-band, before/around the main tick. Good for fast-reflex reactions (defensive recall, alert).

---

## 3. The modules — what each one decides

Each module is `IBotTick` (most) or `IBotRespondToAttack` (one). All of them inherit from `ConditionalTrait` so they can be enabled/disabled via YAML conditions (`enable-ai-v2`, `player.nato`, etc.).

The interesting axes for each module: **what it watches, what it produces, how often it acts, what state it carries over between ticks**.

### 3.1 `BaseBuilderBotModule` — base growth

Picks what to build and where to place it. Owns one `BaseBuilderQueueManager` per build queue category (Building / Defense). Each queue has its own internal countdown:

- `StructureProductionInactiveDelay = 125` ticks (~5s) when nothing is being built
- `StructureProductionActiveDelay = 25` ticks (~1s) between active placements
- `StructureProductionResumeDelay = 1500` ticks after `MaximumFailedPlacementAttempts = 3` placement fails — the famous "AI got stuck and won't build anything for 60 seconds" timer

**Decision flow per tick:** "update cached buildings → for each queue: countdown → choose what to build (by fraction of `BuildingFractions`) → find a placement cell within `MaxBaseRadius` of `initialBaseCenter` → if found, queue `StartProduction` + placement order. If not found, increment failCount."

**Persistent state across ticks:** `initialBaseCenter`, `defenseCenter`, per-queue `failCount`, cached building list. Persisted via `IGameSaveTraitData`.

**Coordinates with:** receives `UpdatedBaseCenter` / `UpdatedDefenseCenter` push notifications from `McvManagerBotModule` and from itself (on attack). Emits the same notifications outward.

**Assumption that breaks for WW3MOD:** the whole model is "base grows outward from a CY". WW3MOD has one fixed Supply Route at spawn; `MaxBaseRadius` becomes a constraint on defense placement only, and `initialBaseCenter` is conflated with the SR location. The placement search still works because it's "find a buildable cell near X", but the strategic intent (expand the base, claim territory by building) is moot.

### 3.2 `UnitBuilderBotModule` — production driver

Tells the production queues *which unit to build next*. Ticks every frame, *acts* every `FeedbackTime = 30` ticks (~1.2 seconds).

**Decision flow:**
1. If any module is pushing `IBotRequestPauseUnitProduction`, stop building entirely.
2. Process at most one external request from `queuedBuildRequests` (filled by `HarvesterBotModule` and `McvManagerBotModule` via `IBotRequestUnitProduction`).
3. For each ground/air queue: if `idleUnitCount < IdleBaseUnitsMaximum = 12` *and* `idleUnitCount < SquadSize`, pick a random buildable unit; else pick by `UnitsToBuild` fractions.

The `IdleBaseUnitsMaximum` gate is the well-known trap — set it lower than `SquadSize` (default 8) and the bot never reaches the threshold to launch a squad, never empties the idle pool, never builds more. WW3MOD's reinforcement model exacerbates this because units arrive at the SR rally with travel time, so the "idle pool count" oscillates.

**Persistent state:** `queuedBuildRequests` FIFO, `idleUnitCount` (pushed in from SquadManager via `IBotNotifyIdleBaseUnits`), per-queue tick counter.

**Coordinates with:** sinks `IBotNotifyIdleBaseUnits` from SquadManager (stale by one tick); sinks `IBotRequestUnitProduction` from Harvester/MCV (also stale).

**WW3MOD note:** the `SkipRearmBuildingCheck` YAML field was added specifically for WW3MOD to bypass `HasAdequateAirUnitReloadBuildings` — an upstream check that assumes 1 helipad per aircraft, which is wrong under our Helipad-is-rearm-support model.

### 3.3 `SquadManagerBotModule` — the closest thing to "strategy"

The biggest, most complex stock module. It collects idle ground units, packs them into `Squad` objects, runs a `FuzzyStateMachine` per squad (states: Idle → AttackMove → Attack → Retreat), and emits orders from inside the squad — not directly. Four independent countdowns, each randomized at trait-enable to stagger across AIs:

- `RushInterval = 600` — try a rush attack early-game
- `AttackForceInterval = 75` — update existing squads (run their FSMs)
- `AssignRolesInterval = 50` — gather new idle units into `unitsHangingAroundTheBase`
- `MinimumAttackForceDelay = 0` — try to spawn a new attack force from the idle pool

**Decision flow on the central path (`AssignRolesToIdleUnits`):**

1. `CleanSquads()` — drop dead/dispersed squads.
2. Push `idleUnitCount` to UnitBuilder.
3. Maybe-rush — if rush countdown hits and `activeUnits.Count(IsIdle) >= RushAttackSquadSize` and we haven't rushed yet, send everything at the enemy CY.
4. Maybe-update — tick each squad's `FuzzyStateMachine`.
5. Maybe-find-new-units — scan `activeUnits` for new arrivals that aren't in `ExcludeFromSquadsTypes` and aren't already squadded.
6. Maybe-create-attack-force — if `unitsHangingAroundTheBase.Count >= SquadSize + rnd(SquadSizeRandomBonus)` (default 8 + 0..30 = 8..38), **drain the entire idle pool** into a new `Squad`, optionally split for pincer if `ThreatMapManager` exists and we have ≥1.5× squad-size units (60% roll).

**Persistent state:** `activeUnits` (every unit it's ever owned), `unitsHangingAroundTheBase` (idle pool), `Squads` (list with FSMs), four countdowns. Persisted to save game.

**Three long-standing quirks worth knowing:**

- (a) `FindNewUnits` filters with `!Info.ExcludeFromSquadsTypes.Contains(name)` — there's a literal TODO comment that says an `IncludeInSquadTypes` opt-in field exists but isn't actually checked. So the inclusion rule is *anti-list only*. Anything not in the exclusion list gets swept into squads.
- (b) `TryToRushAttack` filters `unit.IsIdle` on `activeUnits`. Once squads form, their members aren't idle, so rush effectively fires only in the early game.
- (c) When forming a squad, the entire `unitsHangingAroundTheBase` pool is **drained** — there's a code comment that says "don't bother leaving any behind for defense". This is in direct tension with `LayeredDefenceBotModule`, which expects to find reserve units to assign to slots.

**Coordinates with:** pushes `idleUnitCount` to UnitBuilder; pushes `DefenseCenter` updates to BaseBuilder on defensive engagements; used to read `IsActiveCaptureTarget` from CaptureManager but that hook was removed upstream.

### 3.4 `HelicopterSquadBotModule` — air squads

Helicopter-only squad manager — split from the ground SquadManager because helis have explicit roles (Attack / Scout / Transport) tagged via the `AIHelicopterRole` trait. Holds a reference to `SquadManagerBotModule` because the `Squad` constructor requires one.

Five countdowns: `SquadUpdateInterval = 5` (squad FSM tick), `ScanInterval = 100` (find new helis), `AttackCooldown = 900`, `ScoutInterval = 400`, `TransportInterval = 600`.

**Decision flow:** for each role: scan own helicopters with that role → count those `IsReadyForMission` (ammo + HP) → check squad cap (`MaxActiveSquads = 3`) → if attack role and count ≥ `AttackSquadSize + rnd(AttackSquadSizeBonus)`, form an attack squad targeting `ThreatMap.FindWeakestEnemyCell`. Scout helis pick high-exploration-age cells and emit raw `Move` orders directly. Transport helis chain `EnterTransport` → `Move` → `Unload` for nearby infantry.

**Coordinates with:** uses `BotBlackboard.ClaimUnit(actor, "helicopter")` to mutex helicopters from other modules; reads `ThreatMapManager` for targeting.

### 3.5 `CaptureManagerBotModule` — TECN/engineer capture (stock)

Finds idle capturers and assigns them to enemy/neutral capturable targets. *This is the legacy module*; WW3MOD's `CaptureCoordinatorBotModule` was written to replace it because it has a fatal flaw described below.

Acts every `MinimumCaptureDelay = 375` ticks (~15s).

**Decision flow:**
1. Index own actors matching `CapturingActorTypes` and the `Captures` trait.
2. Filter to those that are idle and not in `activeCapturers`.
3. **Pick a random target player** (not weighted by income, not weighted by strategic value — random) whose relationship is in `CapturableRelationships` (default Enemy|Neutral).
4. Enumerate that player's visible actors filtered by `CapturableActorTypes`, take the top `MaximumCaptureTargetOptions = 10` by sell value.
5. For each capturer, assign to the closest path-reachable target. Queue `CaptureActor` orders.

**The "random player" choice is the killer flaw.** On any map with neutral capturables present, the bot picks "neutral" on some rolls and "enemy" on others — so it sends TECN to capture random structures with no income weighting. WW3MOD's `CaptureCoordinator` fixed this with income-weighted scoring + safety multipliers + escort dispatch.

**Persistent state:** `activeCapturers` list, `capturingActors` index.

**Coordinates with:** nothing. No blackboard, no notifications.

### 3.6 `ScoutBotModule` — exploration + intel

Sends up to `MaxScouts = 2` fast units to old (unexplored) cells. Acts every `ScanInterval = 200` ticks (~8s).

**Decision flow:** initialize once → clean dead scouts → push current scout positions to `ThreatMap.MarkExplored` → call `ReportEnemySightings` to publish intel keys → recruit new scouts up to `MaxScouts` (respecting blackboard claims) → for each idle scout, `FindScoutTarget` picks the highest exploration-age cell (with +500 score bonus for map-edge cells, requiring ≥`MinScoutDistance = 15` from base).

**Persistent state:** `activeScouts` list, `baseCenter`, `initialized` flag. Not save-persisted.

**Coordinates with:** claims units in blackboard with claimant `"scout"`; checks other modules' claims via `IsClaimedByOtherModule` before recruiting. Posts these intel keys: `enemy-base-location`, `enemy-buildings-sighted`, `enemy-vehicles-sighted`, `enemy-infantry-sighted`, `last-scout-tick`. **Nothing reads those intel keys** in stock modules — they exist but go nowhere.

### 3.7 `GarrisonBotModule` — fill garrisonable buildings

Stuffs idle infantry into `GarrisonManager` buildings within `MaxGarrisonRadius = 20` of base. Acts every `ScanInterval = 150` ticks.

**Decision flow:** init once → drop dead buildings → list garrisonable buildings (sort by descending `ThreatMap.GetThreat` if `PrioritizeExposed = true`) → list idle infantry matching `GarrisonActorTypes` (default: anything with `PassengerInfo`) → for each building with vacancy, pick the closest unclaimed infantry, claim it, queue `EnterTransport`. Caps at `MaxOrdersPerTick = 3` per scan.

**Persistent state:** `garrisonedBuildings` count dict, `baseCenter`. Not save-persisted.

**Coordinates with:** writes blackboard `ClaimUnit(infantry, "garrison")`; checks claims before grabbing.

### 3.8 `HarvesterBotModule` — resource economy

Keeps RA-style harvesters busy. Idle-scan every `ScanForIdleHarvestersInterval = 50` ticks; processes at most one `FindAndDeliverResources` re-search per tick (it's expensive).

**Decision flow:** if resource layer is empty, bail. Otherwise: drain one harvester from the needing-orders stack per tick. Every 50 ticks: rebuild the harvester list, refill the stack, and if `harvCount < refineryCount` request a new harvester from UnitBuilder.

**WW3MOD relevance: essentially zero.** WW3MOD has no harvester+refinery economy; the Supply Route handles all reinforcement. The module is still loaded but its scan is a no-op.

### 3.9 `McvManagerBotModule` — MCV deploy

Deploys idle MCVs into Construction Yards, requests new MCVs if CY count < `MinimumConstructionYardCount = 1`. First-tick always runs (initial deploy without moving); after that, scans every `ScanForNewMcvInterval = 20`.

**WW3MOD relevance: zero.** No MCVs exist in WW3MOD. The module is registered because BaseBuilder/SquadManager listen to its `IBotPositionsUpdated` notifications, but it never fires anything.

### 3.10 `BuildingRepairBotModule` — reactive repair

The simplest module. **Not a tick module — `IBotRespondToAttack` only.** When a bot-owned building takes damage that crosses Light → above-Light (one-shot per crossing), queue one `RepairBuilding` order on it. That's the entire module.

**Stateless. Independent. Cleanest design in the bunch.**

### 3.11 `SupportPowerBotModule` — superweapons

Activates support powers (airstrikes, paradrops, nukes) when they cool down. Each power has a `SupportPowerDecision` yaml block that defines an attractiveness scoring function. Per tick, for each ready power: coarse-scan the map in chunks of `CoarseScanRadius`, find regions with `Attractiveness ≥ MinimumAttractiveness`, pick one above-average, fine-scan at `FineScanRadius`, fire on the best cell.

**State:** `waitingPowers` cooldown dict (per-power, persisted), `powerDecisions` dict.

**WW3MOD relevance:** present but unused — no SR-flavored support powers exist yet. Will matter eventually.

### 3.12 `MinelayerBotModule` — minefield placement

Records where the bot is being attacked (via its own `IBotRespondToAttack` hook → fills a 5-slot conflict-position ring) and dispatches `Minelayer` actors to mine chokepoints. Assignment cadence: `ScanTick = 320` ticks, randomized at enable. Will also pick from a `favoritePositions` 5-slot ring of past successful mining spots.

**WW3MOD relevance:** zero unless a faction gets a minelayer unit. Currently dormant.

---

## 4. How the modules "communicate"

Three real channels, ranked by how often they're used:

### 4.1 Push-notification interfaces

Modules implement `IBotPositionsUpdated`, `IBotNotifyIdleBaseUnits`, `IBotRequestUnitProduction`, `IBotRequestPauseUnitProduction` and the engine iterates listeners on the player actor when an event fires. The flow is **always last-tick-stale**: module A pushes on tick N, module B reads what it cached on tick N+1.

Concrete couplings observed:
- `McvManagerBotModule` → `IBotPositionsUpdated` → cached by `BaseBuilderBotModule`, `SquadManagerBotModule` (base center / defense center)
- `BaseBuilderBotModule` → same push on defensive attack
- `SquadManagerBotModule` → `IBotNotifyIdleBaseUnits(unitsHangingAroundTheBase)` → cached by `UnitBuilderBotModule` (idle count)
- `HarvesterBotModule` / `McvManagerBotModule` → `IBotRequestUnitProduction` → enqueued by `UnitBuilderBotModule`

There's no synchronous "module A produces input for module B and B reads it the same tick" anywhere.

### 4.2 `BotBlackboard` — partial mutex

File: `engine/OpenRA.Mods.Common/Traits/BotModules/BotBlackboard.cs`

Defines three things:

- **Unit-claim mutex**: `ClaimUnit(actor, claimant) / ReleaseUnit / GetUnitClaimant`. A unit can be claimed by at most one claimant name; subsequent claims fail. *Used by:* `HelicopterSquadBotModule` (`"helicopter"`), `GarrisonBotModule` (`"garrison"`), `ScoutBotModule` (`"scout"`), and our WW3MOD `SupplyFollowerBotModule` (`"supply-follow"`). *Critically, `SquadManagerBotModule` does not check claims*, so it will happily sweep a scout-claimed humvee into a ground squad.
- **Intel key-value store**: `PostIntel / GetIntel`. *Only `ScoutBotModule` writes; nothing in stock modules reads.* Dead surface.
- **Task board**: `PostTask / ClaimTask / GetOpenTasks / HasTaskNear`. **Zero call sites anywhere.** The infrastructure exists, the API is there, but no module uses it. Task-posting was designed but not wired.

So the blackboard is in practice a **partial unit-claim mutex on opt-in modules**. The most damaging module (SquadManager) is not opted in.

### 4.3 Trait construction order

The order in which `TraitsImplementing<IBotTick>()` returns modules equals YAML declaration order on the player actor. Within a single frame, that's the call order. But because most modules return early on their own countdown, the actual **acting** order varies frame-by-frame — most ticks, only one or two modules are above-countdown and produce orders. So the YAML order matters only when two modules wake up on the same tick.

This is the closest thing we have to "scheduling".

---

## 5. The cadence picture, summarized

| Module | Cadence (ticks) | Cadence (~seconds @ 25 tps) |
|---|---|---|
| BuildingRepair | event-driven | — |
| Scout | 200 | 8s |
| Garrison | 150 | 6s |
| UnitBuilder | 30 | 1.2s |
| SquadManager (assign roles) | 50 | 2s |
| SquadManager (update) | 75 | 3s |
| SquadManager (rush) | 600 | 24s |
| BaseBuilder (active) | 25 | 1s |
| BaseBuilder (inactive) | 125 | 5s |
| HelicopterSquad (squad update) | 5 | 0.2s |
| HelicopterSquad (scan) | 100 | 4s |
| CaptureManager | 375 | 15s |
| McvManager (scan) | 20 | 0.8s |
| Harvester (idle scan) | 50 | 2s |
| SupportPower | per-power cooldown | varies |
| Minelayer | 320 | 13s |

**There is no master cycle.** Every module beats to its own drum. The composite "AI behavior" you observe is the LCM of all these cycles.

---

## 6. Where we can inject

These are the legitimate extension points, in increasing order of intrusiveness.

### 6.1 Add a new `IBotTick` module

Register a new trait implementing `IBotTick` on the player actor. It will be ticked alongside every other module. Position it **first** in the YAML and it sees the world state before any other module has acted in the current frame; position it **last** and it sees the orders queued by every prior module (via `bot.QueueOrder` — note we can't easily *inspect* the queue, only add to it). This is the standard extension pattern; the WW3MOD modules all do this.

**Useful for:** new modules that consume world state and emit orders. Not useful for cross-cutting overrides because the order queue is opaque.

### 6.2 Add a new `IBotRespondToAttack` module

Receives every damage event on bot-owned actors. Out-of-band from the main tick. Stateless or stateful. Used by `BuildingRepairBotModule` and `MinelayerBotModule`.

**Useful for:** reactive behaviors (defensive recall, alert state, threat memory).

### 6.3 Disable a module via `RequiresCondition`

Every module is a `ConditionalTrait`. We already use `enable-ai-v2` / `enable-ai-legacy-only` to fork the AI between legacy and v2 modes. Adding a new condition + a few YAML lines lets us swap the entire module out.

**Useful for:** replacing existing modules without code changes, A/B testing.

### 6.4 Implement the push-notification interfaces

Implement `IBotPositionsUpdated`, `IBotNotifyIdleBaseUnits`, `IBotRequestUnitProduction`, `IBotRequestPauseUnitProduction` and pull the data flowing between stock modules. Useful for tapping into the existing data plane without touching their internals.

**Useful for:** observing what the stock modules are sending each other, vetoing production with `IBotRequestPauseUnitProduction`.

### 6.5 Read / write `BotBlackboard`

Lookup the trait on the player actor and call `ClaimUnit` / `PostIntel` / `PostTask`. The mutex layer works, the intel layer is read-only-by-nothing, the task layer is unused. If we want a coordinator, the unused `PostTask`/`ClaimTask`/`GetOpenTasks` API is already there.

**Useful for:** mutex coordination with the opted-in modules (Heli/Garrison/Scout/SupplyFollower). **Not** useful for coordinating with SquadManager, which ignores the blackboard.

### 6.6 Add a new world trait

`InfluenceMap` and `FrontlineOverlay` already do this — they're not `IBotTick` modules, they're world-scoped traits that compute shared state any module can read. The cost is a per-tick computation outlay; the benefit is one source of truth for all modules.

**Useful for:** shared perception (frontline, threat, exploration). Already partially in place.

### 6.7 Replace `ModularBot` itself

Implement a new `IBot` trait, declare it in YAML instead of `ModularBot`, and we control the whole bot lifecycle. This is the maximum-leverage option: we own the tick loop, the order queue, the module instantiation. The cost is replicating the IBotTick fan-out and the order-rate-limiting (or designing them away).

**Useful for:** wholesale brain replacement — which is what we're considering.

---

## 7. The architectural assumptions worth flagging

Things that are baked in and bite us:

1. **`Actor.IsIdle` as the "is this unit available?" check.** Used by SquadManager (find-new-units, rush-eligibility), CaptureManager, Scout, Garrison, and our recent MountedTransport. `IsIdle` is true when `CurrentActivity == null`, which flips on every activity boundary: between waypoints, during turn-in-place, immediately after a Stop. It's noisy, not a reliable signal of "this unit has nothing to do". Every module that uses it inherits the same bug class.
2. **No goal persistence.** A module decides on tick N: "send this unit to do X". The activity is queued; if it drops for any reason (path-fail, target died, suppression), the unit goes idle and is re-considered on tick N+ScanInterval — possibly by a *different* module. The TECN order-overwriting bug we've been hunting is a textbook case.
3. **The whole-base-drain in SquadManager.** When an attack force forms, every idle unit goes into it. There's no "keep N units back for defense" knob (there's a TODO comment about it). LayeredDefence expects to find reserves; SquadManager empties the pool first.
4. **Production assumes a tech-tree factory model.** UnitBuilder's `UnitsToBuild` fractions, the `IdleBaseUnitsMaximum` gate, the `HasAdequateAirUnitReloadBuildings` check — all assume RA's Barracks/War Factory/Helipad-per-aircraft world. WW3MOD has one SR producing everything; the framework still runs but the assumptions creak.
5. **The intel board is shouting into the void.** Scout posts five intel keys; nothing reads them. Any "AI knows where the enemy base is" reasoning would need a new reader.
6. **The task board is shipped but unused.** `PostTask` / `ClaimTask` exist with zero callers. If we want a task-graph coordinator, the data structure is already there.
7. **CaptureManager's random-player selection.** Picks a random capturable owner per scan, no income or strategic weighting. WW3MOD CaptureCoordinator was built to replace this; the stock module is still wired as a fallback under `enable-ai-legacy-only`.
8. **Modules ignore the blackboard unevenly.** Scout/Heli/Garrison/SupplyFollower opt in; SquadManager/CaptureManager don't. So claims only protect against three out of the ten modules.

---

## 8. Implications for the redesign

Reading this back, the design space for the rewrite collapses into a few choices:

1. **Keep the `IBotTick` fan-out, or replace `ModularBot`?** If we replace `ModularBot` we own the tick loop. We could then implement a single `BotBrain.Tick` that runs as a sequenced pipeline (perceive → plan → assign → order) instead of N independent reflexes. The current modules become methods on the brain, not parallel ticks.
2. **Goal-as-data vs activity-as-truth.** The simplest fix to the "idle flicker" bug class is to track a per-unit `Goal` in our own data structure — `Goal { type, target, until_tick, owner_module }` — and ignore `IsIdle` entirely. Then "is this unit available?" becomes "is its Goal expired or unset?" — a stable signal that doesn't flicker.
3. **Promote the blackboard.** The task-board API is sitting there waiting. If we treat it as the single coordination point and require every module (or every method on the brain) to claim units through it, we close the "two modules want the same unit" loophole.
4. **Keep what works: `InfluenceMap`, `FrontlineOverlay`, the doctrine doc, the condition-gating system, the autotest harness.** None of these have the structural problems above.
5. **Decide on the legacy module disposition.** Most likely course: leave them under `enable-ai-legacy-only`, build the new brain under `enable-ai-v3`, switch the default mode when we're happy. The condition system already supports this.

---

## 9. Files referenced

Engine sources (read-only for our purposes; we don't fork the engine, we extend it):
- `engine/OpenRA.Mods.Common/Traits/Player/ModularBot.cs` — the driver
- `engine/OpenRA.Mods.Common/Traits/BotModules/BotBlackboard.cs` — shared mutex/intel/task store
- `engine/OpenRA.Mods.Common/Traits/BotModules/BaseBuilderBotModule.cs`
- `engine/OpenRA.Mods.Common/Traits/BotModules/UnitBuilderBotModule.cs`
- `engine/OpenRA.Mods.Common/Traits/BotModules/SquadManagerBotModule.cs`
- `engine/OpenRA.Mods.Common/Traits/BotModules/HelicopterSquadBotModule.cs`
- `engine/OpenRA.Mods.Common/Traits/BotModules/CaptureManagerBotModule.cs`
- `engine/OpenRA.Mods.Common/Traits/BotModules/ScoutBotModule.cs`
- `engine/OpenRA.Mods.Common/Traits/BotModules/GarrisonBotModule.cs`
- `engine/OpenRA.Mods.Common/Traits/BotModules/HarvesterBotModule.cs`
- `engine/OpenRA.Mods.Common/Traits/BotModules/McvManagerBotModule.cs`
- `engine/OpenRA.Mods.Common/Traits/BotModules/BuildingRepairBotModule.cs`
- `engine/OpenRA.Mods.Common/Traits/BotModules/SupportPowerBotModule.cs`
- `engine/OpenRA.Mods.Common/Traits/BotModules/BotModuleLogic/MinelayerBotModule.cs`

YAML wiring:
- `mods/ww3mod/rules/ai/ai.yaml` — declaration order on each `ModularBot@<difficulty>` block

Old workspace docs (preserved for reference, not authoritative going forward):
- `WORKSPACE/ai/archive/*.md` — doctrine, foundation, stage docs, handoffs, playtest notes. Useful as historical context for which assumptions we tried before.
