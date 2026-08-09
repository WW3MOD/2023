# Bot module catalogue — every module, what it claims, and whether it still fits

**Researched against `main` @ `4d583f2e`.** `git status -sb`: `main...origin/main [ahead 68]`; `git rev-list --count HEAD..@{u}` = **0** ⇒ the tree is not behind upstream. Static analysis only — **no builds, no game runs, no autotests**. Every factual claim carries a `file:line` that was read at that SHA.

**Who this is for.** You own the mod but do not live in the bot code. This document is the *inventory*: what modules exist, which ones are actually running, what each one grabs, and — separately marked — where an inherited Red Alert design is being asked to do a job it was never built for.

**Timestep.** `mods/ww3mod/mod.yaml:369-371` — default speed `normal` = `Timestep: 60` ms ⇒ **16.667 ticks/s**. Throughout: `seconds = ticks × 0.06`.

**Marker convention.** Everything unmarked is a fact with a citation. Everything beginning **▶ Assessment** is my opinion and you should feel free to overrule it.

**What this document does NOT cover** (deliberately — read these instead):
- How a tick becomes an order, the order funnel, the claim registries and the 2026-08-08 arbitration gate → [`02-lifecycle-and-arbitration.md`](02-lifecycle-and-arbitration.md).
- The influence / belief / danger / control fields the modules read → [`../reference/influence-stack.md`](../reference/influence-stack.md).
- The squad finite-state machines under `BotModules/Squads/` → sibling doc.
- Why there are no factories and what the Supply Route is → [`../reference/game-model.md`](../reference/game-model.md), [`../reference/supply-route.md`](../reference/supply-route.md). **Read those first if you have not.** They are the yardstick this document measures every module against.

---

## 0. The shape of the thing, in one paragraph

There are **24 bot-module classes** in `engine/OpenRA.Mods.Common/Traits/BotModules/`. **19 are instantiated** by `mods/ww3mod/rules/ai/*.yaml` (41 instances) plus one world trait; **5 are never instantiated at all**. Of the 19, **8 were inherited from OpenRA** and **11 were built for WW3MOD**. Two bot profiles ship — `ModularBot@experimental` and `ModularBot@stable` (`ai.yaml:44`, `:49`) — and `@stable` is, without exception, `@experimental` with the newer levers deleted: I diffed every twinned block key-by-key and value-by-value and found **zero divergence on any shared key** (§3). `@stable` is not a different bot; it is the same bot with fewer switches on.

---

## 1. Summary table — every module

Provenance is established from git, not from the copyright header (the headers are copy-paste artifacts and several WW3MOD-original files carry the OpenRA notice). `git log --diff-filter=A` on each file: **"Starting point (#2)" 2023-03-20 = inherited with the Red Alert import**; anything added later is WW3MOD-original.

| # | Module | Provenance | Instantiated? | Profiles | Cadence (shipped) |
|---|---|---|---|---|---|
| 1 | `BotBlackboard` | WW3MOD (2026-03-21) | yes ×1 | both | cleanup every 300 t (18.0 s) |
| 2 | `PoiGoalGuard` | WW3MOD (2026-07-19) | yes ×1 | both | no tick — passive ledger |
| 3 | `CaptureCoordinatorBotModule` | WW3MOD (2026-05-12) | yes ×2 | both (twinned) | 75 t (4.5 s) + 150 t (9.0 s) |
| 4 | `PoiOffensiveBotModule` | WW3MOD (2026-07-19) | yes ×2 | both (twinned) | 100 t (6.0 s) |
| 5 | `PoiGarrisonBotModule` | WW3MOD (2026-07-19) | yes ×2 | both (twinned) | 100 t (6.0 s) |
| 6 | `LaneAmbushBotModule` | WW3MOD (2026-07-25) | yes ×2 | both (twinned) | 100 t (6.0 s) |
| 7 | `LayeredDefenceBotModule` | WW3MOD (2026-05-13) | yes ×2 | both (twinned) | 75 t (4.5 s) |
| 8 | `MountedTransportBotModule` | WW3MOD (2026-05-13) | yes ×2 | both (twinned) | 50 t (3.0 s) |
| 9 | `HelicopterSquadBotModule` | WW3MOD (2026-03-25) | yes ×2 | both (twinned) | five clocks, 5–900 t |
| 10 | `AdaptiveProductionBotModule` | WW3MOD (2026-03-21) | yes ×4 | both (twinned × faction) | 300 t (18.0 s) |
| 11 | `SupplyFollowerBotModule` | WW3MOD (2026-03-21) | yes ×1 | **shared** | 150 t (9.0 s) |
| 12 | `GarrisonBotModule` | WW3MOD (2026-03-21) | yes ×1 | **shared** | 200 t (12.0 s) |
| 13 | `ScoutBotModule` | WW3MOD (2026-03-21) | yes ×2 | **shared** × faction | 200 t (12.0 s) |
| 14 | `EngineerRouteOpenBotModule` | WW3MOD (2026-08-03) | yes ×1 | **exp only** | 100 t (6.0 s) |
| 15 | `ThreatMapManager` (world trait) | WW3MOD (2026-03-21) | yes ×1 | world-level | 90 t (5.4 s) |
| 16 | `SquadManagerBotModule` | **OpenRA**, modified | yes ×4 | both (twinned × faction) | 5 t squad / 600 t rush |
| 17 | `UnitBuilderBotModule` | **OpenRA**, heavily modified | yes ×10 | both, split by profile/faction | `FeedbackTime` 30 t (1.8 s), a `const` |
| 18 | `BaseBuilderBotModule` | **OpenRA**, ~unmodified | yes ×1 | both | every tick + 25/125 t queue delay |
| 19 | `BuildingRepairBotModule` | **OpenRA**, ~unmodified | yes ×1 | both | none — damage interrupt |
| 20 | `CaptureManagerBotModule` | **OpenRA** | **NO** | — | — |
| 21 | `HarvesterBotModule` | **OpenRA** | **NO** | — | — |
| 22 | `McvManagerBotModule` | **OpenRA** | **NO** | — | — |
| 23 | `SupportPowerBotModule` | **OpenRA** | **NO** | — | — |
| 24 | `MinelayerBotModule` | **OpenRA** (upstream merge 2026-03-24) | **NO** | — | — |

Instance count by file: `ai.yaml` 37, `ai-america.yaml` 2 (`:7`, `:54`), `ai-russia.yaml` 2 (`:6`, `:53`), `world.yaml` 1 (`:283`).

---

## 2. The three things that are easy to miss

### 2.1 Instantiated but inert — the most misleading category

A module that appears in `ai.yaml` looks alive. Seven entries are not.

| What | Why it does nothing | Evidence |
|---|---|---|
| **`BaseBuilderBotModule@normal` — the entire construction half** | Its `BuildingFractions` names `hpad, afld, gtwr, pbox, hbox, agun, sam, hsam` (`ai.yaml:1202-1210`). **All eight carry `Prerequisites: ~disabled`** and a repo-wide grep finds nothing that ever grants `disabled`. `ChooseBuildingToBuild` skips any name not in `queue.BuildableItems()` (`BaseBuilderQueueManager.cs:254`), so every fraction is skipped, every cycle, forever. The `ProductionTypes` override path (`:221`) names `supplyroute, hpad, afld` — and `SUPPLYROUTE` is `~disabled` too (`structures.yaml:247`), as the game model requires. | `structures-defenses.yaml:91,187,272,692,777,819`; `structures.yaml:247,432,500`; `ai.yaml:1188-1210` |
| **`EngineerRouteOpenBotModule@experimental`** | Enabled (`RouteOpenEnabled: true`, `ai.yaml:1086`) and fully implemented, but it targets a `LegacyBridgeHut`/`BridgeHut` actor (`CrossingMap.cs:717`). The actors are `bridgehut` / `bridgehut.small` (`civilian.yaml:848,859`) and **zero instances exist across all ten shipped maps** (grepped every `maps/*/map.yaml`). No target ⇒ no mission, ever, on shipped content. | `ai.yaml:1084-1086`; `civilian.yaml:848,859`; `CrossingMap.cs:710-717` |
| **`HelicopterSquadBotModule@stable` — the transport lane only** | `TransportMissionSlots` defaults to 0 (`HelicopterSquadBotModule.cs:120`) and is set **only** on `@experimental` (`ai.yaml:1545`). At 0 the launcher falls through to `activeSquads.Count >= MaxActiveSquads` (`:1015`) — a counter that a transport mission never increments. Three live attack squads block lift permanently. | `HelicopterSquadBotModule.cs:1004-1016`; `ai.yaml:1417,1545` |
| **`SquadManagerBotModule` ×4 — ground branch** | All four instances set `IgnoreGroundUnits: true` (`ai.yaml:1250, 1341, 1800, 1813`), which makes `FindNewUnits` `continue` without claiming (`SquadManagerBotModule.cs:328-334`). The ground FSMs never receive a unit on either profile. | as cited |
| **`SquadManagerBotModule` ×4 — naval branch** | `NavalUnitsTypes` is unset in all four blocks, so `Info.NavalUnitsTypes.Contains(...)` (`:319`) is always false. There is also a `global-disablenavy` condition wired at `ai.yaml:82-84`. | as cited |
| **Four levers inside a live `PoiOffensiveBotModule@experimental`** | `OpportunisticAdvanceEnabled: false` (`ai.yaml:353`), `PreparatoryFires: false` (`:519`), `SuppressionCoordinatedAdvance: false` (`:531`), `FoldShortRangeAdIntoLine: false` (`:595`). ~60 lines of shipped, tested config below them that never executes. | as cited |
| **Five module classes never instantiated at all** | `CaptureManagerBotModule`, `HarvesterBotModule`, `McvManagerBotModule`, `SupportPowerBotModule`, `MinelayerBotModule` — a repo-wide grep of `mods/` finds no trait declaration for any of them (one mention, in a comment at `ai.yaml:118`). | §4 |

▶ **Assessment.** The `~disabled` prerequisite is doing the real design work here and the bot config has not caught up with it. `BaseBuilderBotModule@normal` is 30 lines of `BuildingFractions`, `BuildingLimits`, `MinBaseRadius`, `NewProductionCashThreshold: 5000` and `PlaceDefenseTowardsEnemyChance: 80` that read exactly like a tuning surface and are, every one of them, dead. Someone will tune them. `architecture.md:319` explicitly warns "keep the blocks, the `@normal` suffix is a misnomer" — which is correct about the *trait* (its rally-point half is live) but leaves the impression that the base-building config matters. It does not.

### 2.2 Modules duplicated across profiles — and how they differ

Nine classes are twinned `@experimental` / `@stable`. I diffed each twin pair mechanically (key sets, then value strings, `RequiresCondition` excluded).

| Twin pair | Keys only in `@experimental` | Value divergence on shared keys |
|---|---|---|
| `PoiOffensiveBotModule` (`:260` / `:1652`) | **63** — the whole mission-commitment, opportunistic-advance, fires, retreat, forward-staging, frontline-profile, lateral-spread and evacuation surface | **none** |
| `HelicopterSquadBotModule` (`:1429` / `:1409`) | **24** — solo-attack, strategic pinning, risk-weighted drop, idle evacuation, flight-path hysteresis, `TransportMissionSlots` | **none** |
| `AdaptiveProductionBotModule` (`:932` / `:1733`) | **8** — the whole `CompositionNeed` believed-composition lane | **none** |
| `CaptureCoordinatorBotModule` (`:111` / `:1587`) | **5** — supply-depot capture, `CommitSupportUnits`, `TecnFloorArmyShareCapPct` | **none** |
| `LayeredDefenceBotModule` (`:1033` / `:1771`) | **4** — `ManTheLineEnabled`, `RespectCommitmentLedger`, `CommitLineAssignments` | **none** |
| `MountedTransportBotModule` (`:1147` / `:1113`) | **1** — `CommitPassengers` | **none** |
| `PoiGarrisonBotModule` (`:668` / `:1694`) | **0** | **none — byte-identical** |
| `LaneAmbushBotModule` (`:711` / `:1720`) | **0** | **none — byte-identical** |
| `SquadManagerBotModule` (`:1330` / `:1805`) | **0** | **none — byte-identical** |

The `@stable` key set is a strict subset of `@experimental` in every single case; there is no knob anywhere that `@stable` sets and `@experimental` does not.

▶ **Assessment.** This is a genuinely well-run experiment discipline and it is worth protecting. But note what it means for reading the code: **`@stable` is not "the old bot".** It runs the same `PoiOffensiveBotModule`, the same score-floating axes, the same shared ledger. If you are hunting for "the OpenRA baseline we're trying to beat", it is not `@stable` — that baseline was removed on 2026-07-30 (`architecture.md:319`). The A/B you are running compares a 2026-08 bot against a 2026-08-02 bot.

One naming trap: the `@stable` mounted-transport instance is called **`MountedTransportBotModule@poi`** (`ai.yaml:1113`) — `@poi`, not `@stable` — while its gate is `enable-ai-stable`. Likewise `BaseBuilderBotModule@normal` and `UnitBuilderBotModule@america.normal` are live for both bots despite the `@normal` suffix (their gate is `enable-ai-player`, granted to both, `ai.yaml:58-60`).

### 2.3 The order of blocks in `ai.yaml` is the tick order — and the two profiles are ordered differently

`ModularBot` builds its module array with `p.PlayerActor.TraitsImplementing<IBotTick>().ToArray()` (`ModularBot.cs:112`) and ticks it in array order (`:224-229`). That array follows trait construction order, which follows YAML declaration order. Orders are queued and drained FIFO (`:253`), so within one tick **the module declared later wins a contested unit**, silently. That is now damped — but not replaced — by the order gate merged 2026-08-08 (`RespectCommitmentsOnIssue: true`, `ReorderDwellTicks: 120`, on **both** bots, `ai.yaml:47-48`, `:52-53`); see [`02-lifecycle-and-arbitration.md`](02-lifecycle-and-arbitration.md).

Because the `@experimental` blocks sit at `ai.yaml:111-1146` and the `@stable` twins at `:1587-1816`, with the *shared* modules interleaved in between, **the two profiles run their modules in a different relative order**:

| | `@experimental` tick order | `@stable` tick order |
|---|---|---|
| 1 | `BotBlackboard` (`:74`) | `BotBlackboard` (`:74`) |
| 2 | `CaptureCoordinator` (`:111`) | `BuildingRepair` (`:732`) |
| 3 | **`PoiOffensive` (`:260`)** | `Scout` (`:739`/`:747`) |
| 4 | `PoiGarrison` (`:668`) | **`Garrison` (`:759`)** |
| 5 | `LaneAmbush` (`:711`) | **`SupplyFollower` (`:792`)** |
| 6 | `BuildingRepair` (`:732`) | **`MountedTransport@poi` (`:1113`)** |
| 7 | `Scout` (`:739`/`:747`) | `BaseBuilder` (`:1181`) |
| 8 | **`Garrison` (`:759`)** | `UnitBuilder` ×3 (`:1219`…) |
| 9 | **`SupplyFollower` (`:792`)** | `HelicopterSquad@stable` (`:1409`) |
| 10 | `AdaptiveProduction` (`:932`/`:976`) | `CaptureCoordinator@stable` (`:1587`) |
| 11 | `LayeredDefence` (`:1033`) | **`PoiOffensive@stable` (`:1652`)** |
| 12 | `EngineerRouteOpen` (`:1084`) | `PoiGarrison@stable` (`:1694`) |
| 13 | **`MountedTransport@experimental` (`:1147`)** | `LaneAmbush@stable` (`:1720`) |
| 14 | `BaseBuilder` (`:1181`) | `AdaptiveProduction@stable` (`:1733`/`:1752`) |
| 15 | `UnitBuilder` ×4, `SquadManager` ×2 | `LayeredDefence@stable` (`:1771`) |
| 16 | `HelicopterSquad@experimental` (`:1429`) | `SquadManager@stable` ×2 (`:1787`/`:1805`) |

Note the inversion in bold. On `@experimental`, `PoiOffensive` runs **before** `GarrisonBotModule` and `MountedTransport`. On `@stable`, both of those run **before** `PoiOffensive@stable`.

▶ **Assessment.** This is a real, undocumented asymmetry between the benchmark control and the thing being benchmarked, and it is invisible from either block because the cause is 800 lines of YAML away. Before the order gate it meant the two profiles resolved the same unit contest to *different winners*. It is not clear anyone chose this; it looks like the consequence of appending each new `@experimental` module at the point in the file where its shared cousin already lived, while the `@stable` twins were all appended at the bottom as a group. If the twins are meant to be controlled comparisons, declaration order is part of the configuration and should be mirrored.

---

## 3. Per-module reference

### Group A — never instantiated

These five classes compile into the engine and are reachable from no mod YAML. A repo-wide grep of `mods/` for each trait name returns nothing but one comment (`ai.yaml:118`).

#### A1. `CaptureManagerBotModule` — OpenRA's capture director
- **Purpose.** Sends `Captures`-capable units at capturable structures on a `MinimumCaptureDelay` timer. `CaptureManagerBotModule.cs:20-52`.
- **Provenance.** Inherited (`Starting point (#2)`, 2023-03-20), lightly modified since (6 commits — notably "AI: Don't attack buildings that are being captured by own engineers", 2026-03-21, and the 2026-08-02 case-hardening).
- **Superseded by** `CaptureCoordinatorBotModule`, whose own header still claims it "coexists with the legacy `CaptureManagerBotModule` — experimental YAML gates the legacy ones to `enable-ai-legacy-only`" (`CaptureCoordinatorBotModule.cs:18-19`). That is **stale**: `enable-ai-legacy-only` is granted to nobody (`architecture.md:319`) and the legacy module is not declared at all.

#### A2. `HarvesterBotModule` — OpenRA's ore economy
- **Purpose.** Keeps harvesters harvesting; replaces dead ones. `HarvesterBotModule.cs:22-42`.
- ▶ **Assessment.** Correctly absent. WW3MOD has no resource economy — `PlayerResources` and its `ResourceValues` are commented out on `^BasePlayer` (`player.yaml:4-8`). Leaving the class in the engine costs nothing.

#### A3. `McvManagerBotModule` — OpenRA's base expansion
- **Purpose.** Deploys MCVs into construction yards; builds a new MCV when the yard count drops. `McvManagerBotModule.cs:22-45`.
- ▶ **Assessment.** Correctly absent — there is no MCV and no second base. The corresponding actors are commented out in `mcvs.yaml:47-77`.

#### A4. `SupportPowerBotModule` — OpenRA's superweapon timing
- **Purpose.** Fires support powers according to per-power `SupportPowerDecision` blocks. `SupportPowerBotModule.cs:19-45`.
- ▶ **Assessment.** This is the one absence I would question. WW3MOD ships `MSLO` (`structures-defenses.yaml:1077`) and the mod's doctrine ambitions clearly extend to called-in fires. If a support power ever becomes reachable, nothing on the bot side will use it, and the gap will not announce itself.

#### A5. `MinelayerBotModule` — OpenRA's minefield layer
- **Purpose.** Directs minelayers to lay fields. `BotModuleLogic/MinelayerBotModule.cs`.
- **Provenance.** Arrived with the `release-20250330` upstream merge (2026-03-24) and has never been touched since — it was never part of the original import.

---

### Group B — inherited from OpenRA and still running

#### B1. `BaseBuilderBotModule@normal` — base construction (construction half inert)
| | |
|---|---|
| **Purpose** | Build structures from `BuildingFractions`; set rally points on production buildings. |
| **Provenance** | **Inherited, ~unmodified.** Added `Starting point (#2)` 2023-03-20; the 9 commits since are the RA strip-out, upstream merges, a `RallyPoint` API change and the case-hardening — no behavioural WW3MOD work. |
| **Profiles** | Both (`RequiresCondition: enable-ai-player`, `ai.yaml:1182`). |
| **Cadence** | `IBotTick` with **no countdown** (`:183-189`) — runs every bot tick. The delay lives in `BaseBuilderQueueManager.waitTicks`, reset to 25 (active) / 125 (inactive) `+ world.LocalRandom.Next(0, 10)` (`BaseBuilderQueueManager.cs:103-106`). |
| **Claims** | Own actors carrying `RallyPoint` (`:210-214`) and the `Building` / `Defense` production queues. **No combat units.** |
| **Emits** | `SetRallyPoint` (`:217`); `StartProduction` / `CancelProduction` / `PlaceBuilding` from the queue manager (`BaseBuilderQueueManager.cs:120,160,174`). |
| **Shipped knobs** | `ConstructionYardTypes/VehiclesFactoryTypes/BarracksTypes: supplyroute`, `HeliTypes: hpad`, `AirfieldTypes: afld`, `DefenseTypes: gtwr,pbox,hbox,agun,sam,hsam`, `BuildingFractions` 8 entries, `NewProductionCashThreshold: 5000`, `PlaceDefenseTowardsEnemyChance: 80` (`ai.yaml:1183-1210`). |
| **Status** | **Construction inert** (§2.1). `SetRallyPoint` is the only live effect. |

▶ **Assessment — this is the textbook misfit.** Every field above except `RallyPointScanRadius` encodes the Red Alert model: a base with a radius, a construction yard, a defence perimeter placed toward the enemy, a cash threshold that triggers another factory. WW3MOD has a fixed, indestructible, non-buildable beachhead ([`supply-route.md`](../reference/supply-route.md)) and no construction at all. The module survives because deleting a trait declaration is scarier than leaving it, and because one useful behaviour (rally points) is welded to it. **My recommendation:** either lift the ~15-line `SetRallyPoints` routine somewhere honest and drop the trait, or — cheaper — delete `BuildingFractions`, `BuildingLimits`, `DefenseTypes`, `NewProductionCashThreshold`, `MinBaseRadius`, `MaxBaseRadius`, `MinimumDefenseRadius`, `MaximumDefenseRadius` and `PlaceDefenseTowardsEnemyChance` from `ai.yaml` so the block stops advertising a tuning surface that cannot move.

#### B2. `BuildingRepairBotModule@aiplayer` — repair damaged buildings
| | |
|---|---|
| **Purpose** | On a building crossing from ≤ Light damage to worse, issue `RepairBuilding`. |
| **Provenance** | **Inherited, ~unmodified** (3 commits: import, upstream merge, a null-guard on `e.Attacker` 2026-07-29). |
| **Profiles** | Both (`enable-ai-any`, `ai.yaml:733`). |
| **Cadence** | **None** — it is not `IBotTick`. `IBotRespondToAttack` only (`BuildingRepairBotModule.cs:23`), an interrupt on the damage-state transition. |
| **Claims** | Nothing. `self` is the damaged building. |
| **Emits** | `RepairBuilding` (`:44`), non-queued. |
| **Config** | None — the Info class is empty (`:18-21`). |

▶ **Assessment.** Harmless and near-free, but its scope is now very small: the Supply Route is indestructible, no defences are buildable, and the remaining repairable buildings a bot owns are captured neutrals. Worth knowing it exists so you do not go looking for a repair system that is not there.

#### B3. `SquadManagerBotModule` ×4 — fixed-wing air squads (ground/naval branches dead)
| | |
|---|---|
| **Purpose** | Originally OpenRA's whole combat brain: pool every combat unit, form squads, run attack/idle/fleet FSMs. In WW3MOD it retains **only** the air branch. |
| **Provenance** | **Inherited, heavily modified** (16 commits: multi-axis split, the `IgnoreGroundUnits` carve-out for `PoiOffensiveBotModule`, the Phase-4b role migration, case-hardening). |
| **Instances** | `@experimental.russia.fixedwing` (`:1239`), `@experimental.america.fixedwing` (`:1330`), `@stable.russia.fixedwing` (`:1787`), `@stable.america.fixedwing` (`:1805`) — **byte-identical per faction across profiles**. |
| **Cadence** | Squad FSM update 5 t; `RushInterval: 600` (36 s) (`ai.yaml:1243`). |
| **Claims** | `World.ActorsHavingTrait<IPositionable>()` owned, not in `ExcludeFromSquadsTypes` (`SquadManagerBotModule.cs:302-307`). Air membership is `resolver.GetRole(a) == UnitRole.AttackAir` and not a helicopter, under `UseUnitRoles: true` (`:356-358`); the fallback `AirUnitsTypes` name list (`mig, frog` / `a10, f16`) is only used with roles off. Ground candidates hit `IgnoreGroundUnits` and are skipped **without** being claimed (`:328-334`) so `PoiOffensiveBotModule` sees them. |
| **Emits** | Nothing from this file directly — the squad FSMs under `Squads/States/` issue the orders. |

Historical note worth keeping: `AirUnitsTypes` used to be UPPERCASE in `ai.yaml` while actor names are lowercased at ruleset load, so this name list matched nothing and **no fixed-wing squad formed on any profile** (`WORKSPACE/bugs/discovered.md`, 2026-07-24). Fixed twice over — the YAML is lowercase now (`ai.yaml:1245`) and `ActorNameCase.NormalizeInPlace` runs at `RulesetLoaded` (`SquadManagerBotModule.cs:108`).

▶ **Assessment.** What is left is a 565-line class plus a whole `Squads/` FSM directory serving two airframes per faction with `SquadSize: 2`. The ground FSMs, `unitsHangingAroundTheBase`, `AttackOrFleeFuzzy`, the naval branch and the protection logic are all carried but unreachable. **This is the second-clearest candidate for a purpose-built replacement**: an air-tasking module that thinks in sorties, targets and rearm cycles would be a fraction of the size and would not drag OpenRA's "squad hangs around the base until it is big enough, then rushes" model into a game with no base to hang around.

#### B4. `UnitBuilderBotModule` ×10 — the call-in economy
| | |
|---|---|
| **Purpose** | Decide *what* to call in from off-map reserves and push it into a production queue. In WW3MOD this is not manufacturing — it is budget allocation against the Supply Route ([`game-model.md`](../reference/game-model.md)). |
| **Provenance** | **Inherited, heavily modified** — 22 commits, most of them 2026-07/08 WW3MOD work (composition-directed purchasing, priority-production seam, transport gating, need-gated resupply, threat-scaled AA). |
| **Instances** | Ground: `@america.normal` / `@russia.normal` (Stable, `enable-ai-player && !enable-ai-experimental`) and `@america.experimental` / `@russia.experimental` — in `ai-america.yaml:7,54` / `ai-russia.yaml:6,53`. Air: `@america.fixedwing` `@russia.fixedwing` (**shared**, `enable-ai-any`), `@america.heli` `@russia.heli` (Stable-only via `!enable-ai-experimental`), `@experimental.america.heli` `@experimental.russia.heli`. |
| **Cadence** | Not a YAML field. `ticks++; if (ticks % FeedbackTime == 0)` with `public const int FeedbackTime = 30` (`UnitBuilderBotModule.cs:223,378`) = **1.8 s, unconfigurable**. |
| **Claims** | No units. It reads its own idle-unit count and orders the queue actor. |
| **Emits** | `Order.StartProduction` (`:481`, `:671`). |
| **Key knobs** | `UnitsToBuild` weights, `UnitLimits`, `UnitDelays`, `IdleBaseUnitsMaximum` (0 for fixed-wing, 8 for helis), `SkipRearmBuildingCheck: true` everywhere, `GateTransportOnDemand`/`TransportMinPassengers: 4` (experimental heli only), plus the experimental ground twins' `CompositionDirected`, `UnitTargetShares`, `UnitRoles`, `CounterMatrixPct`, `GateResupplyOnAmmoNeed`, `ScaleAntiAirToThreat`. |

The single most important thing to understand here is documented in the mod YAML itself and is easy to misread: **a `UnitsToBuild` weight is a share CEILING, not a priority** — any weight ≥ 100 never binds, so it only marks a type "always eligible", and the real cap is `UnitLimits` (`ai-america.yaml:60-65`). The `@experimental` ground twins replace the resulting uniform lottery with `CompositionDirected` (`:117`) + `UnitTargetShares` (`:143`, per-mille of army *value*, summing to 1000), `UnitRoles` (`:178`) and a believed-enemy `CounterMatrixPct` bias (`:202-205`).

▶ **Assessment.** The `@experimental` half of this is genuinely good and clearly WW3MOD-native — census the army you own, buy the class furthest below target, bias by *believed* enemy composition. The problem is the substrate underneath it. `UnitsToBuild`, `UnitLimits`, `UnitDelays`, `IdleBaseUnitsMaximum` and the `FeedbackTime` const are all OpenRA production-queue concepts, and `@stable` still runs on them alone. The `@america.normal` block is a 45-line weight table whose weights, per the mod's own comment, mostly cannot bind. A budget-allocation model would say "you have $X of standing reserve, here is the force shape you want, here is the delivery cadence" — and would not need `IdleBaseUnitsMaximum` at all.

---

### Group C — WW3MOD-built, shared by both profiles

These three run on a single `enable-ai-any` instance, so a change here changes `@stable` immediately. There is no twin to protect the benchmark.

#### C1. `SupplyFollowerBotModule@supply` — field resupply logistics
| | |
|---|---|
| **Purpose** | Drive `truk` supply trucks to clusters of friendly units that need ammo; retreat them out of danger; drop static supply caches. |
| **Provenance** | **WW3MOD** (2026-03-21, 23 commits — the most actively worked module in the repo after `PoiOffensive`). |
| **Profiles** | **Shared, both** (`enable-ai-any`, `ai.yaml:793`). Experimental-only behaviours are double-gated in C#. |
| **Cadence** | `ScanInterval` C# default 120 (`:28`), **shipped 150** = 9.0 s (`ai.yaml:795`, countdown `:420-425`). |
| **Claims** | Trucks only — `SupplyTruckTypes: truk` (`ai.yaml:794`), tested at `:489`. It *reads* other owned `Mobile` actors to build clusters but never orders them (`:510`). |
| **Emits** | `Move` ×6 (`:711`, `:742`, `:744` queued, `:767`, `:1267`, `:1582`), `Stop` (`:858`), `DropSupplyCacheAt` (`:911`). |
| **Key knobs** | `MaxFollowDistance: 35`, `MinNearbyFriendlies: 4`, `SectorSpread: true`, `SmallSquadCoverage: true`, `DangerEvac: true`, **`EvacDangerThreshold: 60`**, `EvacRetreatCells: 12`, `EvacDwellScans: 1`, `EvacReleaseHysteresis: 15`, `IdleTruckHunt: true`, `HuntStarvingThresholdPerMille: 250`, `HuntLeashCells: 20`, `DropAndLeave: true`, `DropMinStarvingUnits: 3`, `DropMinSupply: 250` (`ai.yaml:794-926`). |

▶ **Assessment — this is the module that produced the headline example, and it is worth understanding as a *class* of bug, not a one-off.** `EvacDangerThreshold` is declared `= 60` at `SupplyFollowerBotModule.cs:91` and set to `60` at `ai.yaml:830`. It is compared against `GroundDangerAt(truck.Location)` (read at `:1530`, compared at `:1540`) — a `DangerFieldLayer` reading whose **observed median at the moment of evac entry is 66,834**, measured from the user's own play log (`WORKSPACE/recon/260809-truck-loop-from-live-log.md:93,211-212`). The threshold is exceeded by roughly three orders of magnitude at all times, so `DangerEvac` is not a danger response — it is permanently on. Note the shape: nothing is *wrong* in either file. The constant is plausible, the field is correct, and the two were simply never introduced to each other after the field was rescaled. `EvacReleaseHysteresis: 15` has the same defect and its own `[Desc]` block already admits it (`:109-114`). Because this instance is `enable-ai-any`, **both profiles have been evacuating trucks unconditionally** — the 2026-07-30 commit that added it was titled "@experimental" but set the flag on the shared block (`260807-supply-truck-oscillation.md:158`).

#### C2. `GarrisonBotModule@defenses` — garrison infantry into buildings
| | |
|---|---|
| **Purpose** | Put idle passengers into `GarrisonManager` buildings near the base when a believed threat appears. |
| **Provenance** | **WW3MOD** (2026-03-21, 10 commits). |
| **Profiles** | **Shared, both** (`enable-ai-any`, `ai.yaml:760`). |
| **Cadence** | C# default 150 (`:33`), **shipped 200** = 12.0 s (`ai.yaml:761`). No stagger. |
| **Claims** | `GarrisonActorTypes` is **unset in mod YAML**, so `IsGarrisonEligible` falls through to "any actor with `PassengerInfo`" (`GarrisonBotModule.cs:483-492`) — narrowed per building by a `CanEnter` cargo-type match at the pairing site. Plus owned + `Mobile` + `IsIdle` + not blackboard-claimed + (experimental) not ledger-committed (`:283-287`). |
| **Emits** | `EnterTransport` (`:322`), capped by `MaxOrdersPerTick: 2` (`ai.yaml:763`); `Unload` on release (`:475`). |
| **Key knobs** | `MaxGarrisonRadius: 25`, `PrioritizeExposed: true`, `RequireBelievedThreat: true`, `MinBelievedDanger: 1`, `ReleaseWhenThreatClears: true`, `MinGarrisonDwellTicks: 750`, `CommitGarrisonedUnits: true`. |

Two things the name hides, both stated outright in the trait's own `[Desc]` (`GarrisonBotModule.cs:18-23`): the instance is called **`@defenses`** but no defensive structure is buildable, so `^CivBuilding`'s `GarrisonManager` means the building side is **dominated by neutral civilian houses**; and the unit side is not infantry-only. This module previously froze supply trucks permanently — `^WheeledVehicle` grants `Passenger`, so `truk` qualified, the `EnterTransport` was silently discarded by the cargo-type check, and the truck was blackboard-claimed with **no `ReleaseUnit` anywhere in the file**. Fixed 2026-08 with a per-building `CanEnter` match and a real claim lifecycle (`WORKSPACE/bugs/discovered.md`, entry 22).

▶ **Assessment.** The current implementation is careful and the fix was the right one, but step back: this is *house-garrisoning* wearing the name `@defenses`, running on both profiles, competing for idle infantry with `PoiOffensive`, `LayeredDefence`, `LaneAmbush` and both transports. `MinGarrisonDwellTicks: 750` (45 s) is a long time to have a rifleman inside a civilian building. Whether occupying houses is doctrine WW3MOD wants is a design question that the name has been quietly answering "yes" to.

#### C3. `ScoutBotModule@america` / `@russia` — map exploration and enemy intel
| | |
|---|---|
| **Purpose** | Send up to 2 fast units to unexplored areas; post enemy sightings to the blackboard. |
| **Provenance** | **WW3MOD** (2026-03-21, only 2 commits — it has barely been touched since it was written). |
| **Profiles** | **Shared, both**, faction-split (`enable-ai-any && player.nato` / `player.brics`, `ai.yaml:740`, `:748`). |
| **Cadence** | `ScanInterval: 200` = 12.0 s (`ai.yaml:743`). **No stagger** — the countdown starts at 0 and fires on the first bot tick. |
| **Claims** | `ScoutTypes: humvee` (NATO) / `btr` (BRICS), owned + `Mobile` + name match + `IsIdle` + not blackboard-claimed (`ScoutBotModule.cs:139-146`). `MaxScouts: 2`. Both types are in the POI stack's `ExcludeUnitTypes`, so the overlap is deconflicted by design. |
| **Emits** | `Move` per idle scout (`:128`). |
| **Side effects** | `threatMap.MarkExplored(...)` (`:112`, `:131`); `blackboard.PostIntel(...)` (`:287-290`). |

▶ **Assessment — this is my third fitness concern and I think it is under-appreciated.** The intel product is thin in three separate ways. (1) It counts enemies with `world.FindActorsInCircle(scout.CenterPosition, 8 cells)` and filters only on relationship (`:244-260`) — it never checks visibility or cloak, so it is *geometrically* bounded rather than fog-legal. (2) `PostIntel` is `intel[key] = value` (`BotBlackboard.cs:246`) — a plain **overwrite with no accumulation and no decay**. The three counters therefore mean "whatever the *last* scout saw in *its* 8-cell circle on the *last* 12-second scan", and once written they never expire; if both scouts die the numbers stand forever. (3) That stale number is a **hard gate on the whole legacy counter-buy lane**: `AdaptiveProductionBotModule` early-returns when `totalSightings < MinEnemySightings` (`:255-256`) reading exactly those blackboard values, and only *afterwards* runs its own genuinely fog-legal whole-map `ScanEnemyComposition()` (`:259`, `:594-627`). The module owns a correct sensor and refuses to consult it until a much worse one says the enemy exists. Filed as a new bug (§5).

---

### Group D — WW3MOD-built, twinned across both profiles

These are the modern strategic layer. All were written for WW3MOD against the Supply Route model; none assumes production queues or base building. I give each a compact entry — the *design* of the POI stack is covered in the sibling documents.

#### D1. `PoiOffensiveBotModule` — score-floating attack axes (4,354 lines, the biggest module in the repo)
| | |
|---|---|
| **Purpose** | Split the general ground army across `PoiMap`-scored enemy objectives instead of forming one death-ball. Enemy income structures, the enemy Supply Route circle, and the enemy base all compete on the **same** score with no privileged base-beeline (`PoiOffensiveBotModule.cs:3-13`). |
| **Provenance** | **WW3MOD** (2026-07-19, **59 commits** — by far the most-worked file). |
| **Instances** | `@experimental` `ai.yaml:260`, `@stable` `:1652`. |
| **Cadence** | `ReevaluateInterval: 100` = 6.0 s (`:58`, `ai.yaml:262`); initial countdown randomised at enable (`:1050`). |
| **Claims** | `BuildFreePool()` (`:1932-1942`) = `IsEligibleCombatUnit` minus axis-claimed minus ledger-committed. The predicate (`:2317-2420`): owned/alive/in-world, has `IPositionable` **and** `AttackBase`, **not** `Aircraft`, not out-of-ammo (under `SkipOutOfAmmoUnits`), not evacuating, **not** `CrewMember`, and under `UseUnitRoles` role ∈ {`MainBattle`, `IndirectFire`} and **not** a troop carrier. Notably it does **not** filter on `IsIdle`. |
| **Emits** | `AttackMove` ×7 (`:2300`, `:3070`, `:3073`, `:3187`, `:3503`, `:4027`, `:4056`), `SetUnitStance` ×3 (`:3238`, `:3250`, `:3257`), `SetCohesion` (`:3055`). **Plus two direct `QueueActivity(false, new RotateToEdge(...))` calls that bypass the order funnel entirely** — out-of-ammo evacuation (`:2510`) and ejected-crew evacuation (`:2596`). |
| **Live knobs (both)** | `UnitsPerAxis: 8`, `MinAxisSize: 3`, `MaxAxes: 4`, `AxisCommitmentTicks: 250`, `EarlyGameSpread` (4500 t), `SrPressureScoreMultiplier: 260`, `DangerFieldRouting`, `StrategicRepointEnabled`, `FiresStandoff`, `EchelonPositioning`, `CohesionSwitchEnabled` (Spread→Tight at 15 cells). |
| **Exp-only** | 63 further keys — mission commitment, retreat/reengage force ratios, forward staging, the frontline profile + man-the-line bias, lateral spread, reachability gating, continuous bombardment, ammo/crew evacuation, an `Aggressiveness: 50` slider. |

▶ **Assessment.** Architecturally this is the right shape for WW3MOD and it is the piece to build *toward*, not away from — decision math is factored into pure `*Math` classes with NUnit pins so it can port to a future brain, and the unit claim is a real ledger rather than an `IsIdle` guess. My reservation is size: 4,354 lines with ~90 Info fields, five default-off levers and a comment-to-code ratio that is high even by this repo's standards. It is close to the point where the honest move is to split the fires/artillery executor and the staging/retreat executor out of the axis allocator.

#### D2. `PoiGarrisonBotModule` — hold captured money POIs
- **Purpose:** park 1–3 units on each owned income POI, scaled by value and enemy pressure (`PoiGarrisonBotModule.cs:3-11`). **WW3MOD** (2026-07-19).
- **Instances:** `@experimental` `ai.yaml:668`, `@stable` `:1694` — **byte-identical**.
- **Cadence:** `ReevaluateInterval: 100` = 6.0 s. **Claims:** the same free-pool shape as offense, deconflicted only by the shared ledger. **Emits:** one grouped `AttackMove`.
- **Knobs:** `ValuePerGarrisonUnit: 50`, `MinGarrison: 1`, `MaxGarrison: 3`, `MaxGarrisons: 4`, `GarrisonCommitmentTicks: 250`, `DefendRepointEnabled` + believed-danger multipliers (calm 100 / probed 150 / assaulted 250).
- ▶ **Assessment.** Well-scoped and deliberately small (≤12 units) so it cannot starve offense. No complaints.

#### D3. `LaneAmbushBotModule` — concealed posts on the reinforcement corridor
- **Purpose:** post a handful of ambush-capable units on the lane between the two beachheads and grant them `enable-ambush-tactics` (`LaneAmbushBotModule.cs:3-10`). **WW3MOD** (2026-07-25).
- **Instances:** `@experimental` `ai.yaml:711`, `@stable` `:1720` — **byte-identical**.
- **Cadence:** 100 t = 6.0 s. **Claims:** owned + `AttackBase` + non-`Aircraft` + `CanHostAmbush` + role-filtered + not ledger-committed. **No `IsIdle` filter.** Bounded to `MaxAmbushes: 2 × UnitsPerAmbush: 2` = **4 units**.
- **Emits:** grouped `AttackMove` + `SetUnitStance`; also grants/revokes the `enable-ambush-tactics` `ExternalCondition` **directly on the unit**, bypassing the order funnel (`:459-462` grant, `:484` and `:202` revoke).
- ▶ **Assessment.** Doctrinally the most "modern battlefield" module in the set, and its header is the best-written in the repo — it names three carried observations, including the fact that the `^AutoTargetGround` family has no ambush seam and is therefore auto-excluded. Note it is a 624-line module governing at most four units.

#### D4. `LayeredDefenceBotModule` — reserve-driven line filling
| | |
|---|---|
| **Purpose** | Read `InfluenceMap.GetFrontline(player)`, score contested cells by "our line is thin AND the enemy is weak", and send **reserve** units there — screens to the slot, main-line units to a standoff shifted back toward our own SR (`LayeredDefenceBotModule.cs:4-17`). |
| **Provenance** | **WW3MOD** (2026-05-13, 18 commits). |
| **Instances** | `@experimental` `ai.yaml:1033`, `@stable` `:1771`. |
| **Cadence** | `ScanInterval: 75` = 4.5 s, staggered at enable (`:208`). Hard-gated on `influenceMap != null` (`:235`). |
| **Claims** | Owned + alive + **`IsIdle`** + role `MainBattle` + past the per-unit `AssignCooldownTicks: 250` + not reserved by `MountedTransport` + (exp) not ledger-committed. Cap `MaxAssignsPerScan: 6`. |
| **Emits** | `AttackMove` per unit, both tagged `BotOrderDamping.Recurring` (`:511` density path, `:650` man-the-line path). |
| **Exp-only** | `ManTheLineEnabled` + `ManTheLineMinThreat`, `RespectCommitmentLedger`, `CommitLineAssignments`. |

Its own header says "when the frontline is empty, this module does nothing — existing `SquadManagerBotModule` handles opening play" (`:28-29`) and the code implements that (`:322-323`). **The second half of that sentence is now stale**: `SquadManagerBotModule` sets `IgnoreGroundUnits` on every instance and handles no ground unit at all. Opening play is `PoiOffensiveBotModule`'s `EarlyGameSpread`.

#### D5. `MountedTransportBotModule` — the IFV/APC infantry ferry
| | |
|---|---|
| **Purpose** | Pair idle IFVs with infantry reserves, drive to the thinnest cell of *our own* frontline, drop off, return (`MountedTransportBotModule.cs:3-21`). |
| **Provenance** | **WW3MOD** (2026-05-13, 17 commits). |
| **Instances** | `@poi` = **Stable** (`enable-ai-stable`, `ai.yaml:1113`), `@experimental` (`:1147`). |
| **Cadence** | C# default 100 (`:41`), **shipped 50** = 3.0 s, randomised at enable (`:302`). |
| **Claims** | Carriers: `bradley, bmp2, m113`, must have `Cargo`, be **empty**, not already tasked — deliberately **not** `IsIdle` (`:521`, PITFALL comment above it). Passengers: 20-name `PassengerTypes` allowlist within `ReserveZoneRadiusCells: 14` of the own SR, or (exp) within `PickupCorridorCells: 6` of the SR→drop lane (`:581-584`). |
| **Emits** | `Move` ×3, `Stop` ×2, `EnterTransport` ×2, `Unload`, `CaptureActor` (the capture-ferry path). |
| **Batching** | Real: `MinPassengersPerLoad: 2` / `MaxPassengersPerLoad: 5`, and it waits (`LoadingTimeoutTicks: 1500`). |
| **Exp-only** | `CommitPassengers`. |

Two documented traps carried here: the **14-cell pickup bubble around the own SR means a soldier that walks more than 14 cells from home can never be picked up again by anything, for the rest of the match** (`260808-transport-census.md` §0.4) — the first leg out of the beachhead is the only leg a transport ever serves; and `UnloadOnArrival` exists because the module used to issue `Order("UnloadCargo")`, which `Cargo.ResolveOrder` does not handle, so carriers sat at the drop-off loaded forever (`WORKSPACE/bugs/discovered.md`, entry 80).

▶ **Assessment.** The mechanism is sound; the *demand model* is the gap and it is shared with the helicopter lift. Neither ferry asks where any soldier needs to go — each computes its own single destination per pass and then releases the passengers, whereupon the offense stack walks them somewhere else. That is supply-driven transport in a game whose whole geometry is "units arrive at a fixed beachhead and must get to a POI", which is the most demand-shaped transport problem an RTS can have.

#### D6. `HelicopterSquadBotModule` — attack helis, scouting helis, air lift
| | |
|---|---|
| **Purpose** | Role-based helicopter management: attack squads with hit-and-run, scout sorties, and infantry lift. |
| **Provenance** | **WW3MOD** (2026-03-25, 25 commits). |
| **Instances** | `@stable` `ai.yaml:1409`, `@experimental` `:1429`. |
| **Cadence** | **Five per-call countdowns in one tick**, none staggered: `SquadUpdateInterval: 5` (0.3 s, `:146`), `ScanInterval: 100` (6.0 s, `:143`), `AttackCooldown: 900` (54 s), `ScoutInterval: 400` (24 s), `TransportInterval: 600` (36 s). Plus an unconditional per-tick idle evaluation. |
| **Claims** | Any owned actor with the `AIHelicopterRole` trait (`:559`), claimed in the blackboard as `"helicopter"` (`:569`). Lift passengers: infantry with `WithInfantryBody`, role `MainBattle` (`RestrictLiftToLineInfantry` defaults **true**), not reserved by `MountedTransport`, not ledger-committed, **within 14 cells of the own SR**. |
| **Emits** | `Move` ×4, `Unload` ×2, `EnterTransport`, `Stop` — plus one direct `h.QueueActivity(false, new RotateToEdge(...))` at `:1735` that **bypasses the order funnel entirely**. |
| **Exp-only** | 24 keys — solo attack heli, income-gated pair-up, strategic target pinning, risk-weighted drop-site selection, idle/forward evacuation, flight-path hysteresis, careful scout employment, and `TransportMissionSlots: 1`. |
| **Status** | Attack + scout lanes live on both. **Lift lane inert on `@stable`** (§2.1). |

#### D7. `CaptureCoordinatorBotModule` — capture income structures with escort and defence
| | |
|---|---|
| **Purpose** | Income-weighted capture target selection, escorted dispatch, and a defence pass that summons defenders to threatened owned structures (`CaptureCoordinatorBotModule.cs:3-17`). |
| **Provenance** | **WW3MOD** (2026-05-12, 33 commits). Replaces the inherited `CaptureManagerBotModule`. |
| **Instances** | `@experimental.tecn` `ai.yaml:111`, `@stable.tecn` `:1587`. |
| **Cadence** | **Two countdowns**: `ScanInterval: 75` (4.5 s) and `DefenseScanInterval: 150` (9.0 s), both randomised at enable. |
| **Claims** | Capturers: `CapturingActorTypes: tecn,tecn.russia,tecn.america`, rebuilt from `UnitRole.CaptureSpecialist` under `UseUnitRoles`, requiring `IsIdle` and no ledger commit. **Escorts and defenders: any armed idle owned unit within `SupportRecruitRadiusCells: 40`** — `SupportingUnitTypes` is unset, so there is no whitelist. |
| **Emits** | `CaptureActor` **queued** (`:1249`), `Move` (`:1135`, `:1374` retreat-to-SR), grouped `AttackMove` for escorts (`:1514`) and defenders (`:1625`). |
| **Key knobs** | `IncomeWeights` oilb 50 / fcom 100 / bio 150 / miss 10 / hosp 20 (mirroring the `CashTrickler` amounts), `DistanceHalfLifeCells: 20`, safety multipliers 100/40/10, `EscortSize: 2` → `ContestedEscortSize: 4`, `TecnFloor: 1` scaling to `TecnFloorMax: 5`, `UseTransportForDistantCaptures` at ≥12 cells, `RetreatCapturerWhenDone`, `StageIdleCapturers` + `ReserveStandoffCells: 10`. |
| **Exp-only** | `CaptureSupplyDepots` + `SupplyDepotActorTypes: logisticscenter`, `CommitSupportUnits`, `TecnFloorArmyShareCapPct: 100` (100 = inert by its own `[Desc]` at `:126`). |

▶ **Assessment.** This is the module most directly aligned with the WW3MOD economy — capture *is* the economy — and it shows in the care taken. The one thing I would flag for a reader: escorts and defenders are recruited by "any armed idle owned unit within 40 cells", which at 40 cells from a POI near the beachhead reaches deep into the reserve pool that `PoiOffensive`, `LayeredDefence` and both transports are also drawing from. Its 33 commits and the 4-tier escort sizing suggest that contention has been felt.

#### D8. `AdaptiveProductionBotModule` ×4 — reactive counter-buying
| | |
|---|---|
| **Purpose** | Watch enemy composition and request counter-units through `IBotRequestUnitProduction`. **It never orders a unit** — zero `QueueOrder` sites in the file. A budget actor, not an order actor. |
| **Provenance** | **WW3MOD** (2026-03-21, 7 commits). |
| **Instances** | `@experimental.america` `:932`, `@experimental.russia` `:976`, `@stable.america` `:1733`, `@stable.russia` `:1752`. |
| **Cadence** | C# default 500 (`:24`), **shipped 300** = 18.0 s. |
| **Lanes** | (1) **SR defense** (exp only) — classify believed contacts near an owned SR from the fog-legal `BeliefStore` and pre-buy the matched counter, bypassing `MinEnemySightings`; reserves ≤1 request/cycle. (2) **Composition need** (exp only) — score believed armour/infantry/air/AA-weakness and buy the biggest need. (3) **Legacy scouted composition** (both) — gated on the blackboard sightings, then a priority sort over `AntiVehicleUnits` / `AntiInfantryUnits` / `AntiAirUnits`. |
| **Key knobs** | `MaxRequestsPerCycle: 2`, `MinEnemySightings: 3`, `SupplyRouteScanRadius: 10` with armor 1200 / air 1000 / infantry 1000 value thresholds, `RouteToEnabledProducer: true`; exp adds `CompositionNeedEnabled`, four `*NeedWeight: 100`, `AaWeakThreshold: 2000`, `NeedBudgetReservePct: 200`, `AirStrikeUnits`. |

`RouteToEnabledProducer` exists because a *condition-disabled* `UnitBuilder` twin still answers the `IBotRequestUnitProduction` interface but its `BotTick` never runs, so a request handed to it is silently lost (`:87-90`, `AdaptiveRoutingMath.cs:7`). With ten `UnitBuilder` instances split by profile and faction, that was a real failure mode.

▶ **Assessment.** The `@experimental` belief-store lanes are the right design. Lane (3), which is all `@stable` has, is gated on the blackboard intel described in C3 and inherits every weakness of it.

---

### Group E — support traits (no `IBotTick` order issuance)

#### E1. `PoiGoalGuard@poi` — the shared commitment ledger
- **Purpose:** a per-unit record "unit U is pursuing objective O until tick T", so a module does not re-issue an order every scan when a unit's `IsIdle` flag momentarily flickers (`PoiGoalGuard.cs:3-18`). Objectives are namespaced strings — `capture:<id>`, `offense:<id>`, `defend:<id>`, `ambush:<id>`, `bridge-repair:<id>`, `tacpos:<id>`.
- **Provenance:** **WW3MOD** (2026-07-19). **Instance:** one, shared (`enable-ai-experimental || enable-ai-stable`, `ai.yaml:100-101`).
- **Config:** `DefaultCommitmentTicks: 600` (C# default 300, `:314`) — raised because at Speed 25 one cell is ~41 ticks, so 300 only covered a ~7-cell walk (`ai.yaml:102-106`).
- **Two known sharp edges,** both acknowledged in `OrderArbitrationMath.cs:21-27` and `WORKSPACE/bugs/discovered.md` (2026-08-09): `Commit` with a *different* objective silently overwrites the incumbent claim (`PoiGoalGuard.cs:68-76`), and `Release` is keyed on the **actor**, not the objective (`:100`), so a caller deletes whichever claim the actor happens to hold.

#### E2. `BotBlackboard@ai` — task board and claim registry
- **Purpose:** a task list (`BotTaskType` × `BotTaskStatus`), a unit-claim registry (`ClaimUnit`/`ReleaseUnit`, `:196`, `:214`) and a free-form intel dictionary (`PostIntel`/`GetIntel`, `:244`, `:256`).
- **Provenance:** **WW3MOD** (2026-03-21, **1 commit** — written once and never revisited). **Instance:** one, shared (`enable-ai-any`).
- **Config:** `TaskStaleTicks: 1500` (90 s), `CleanupInterval: 300` (18 s).
- **What is actually used:** only `ClaimUnit` and the intel channels, and only by the older support modules — `HelicopterSquadBotModule`, `GarrisonBotModule`, `ScoutBotModule`, `SupplyFollowerBotModule`, `AdaptiveProductionBotModule` (`WORKSPACE/DISCOVERIES.md:2335`). **The `BotTask` half is written by nobody.**
- ▶ **Assessment.** Two parallel claim registries — this one and `PoiGoalGuard` — honoured by disjoint sets of modules. The modern POI stack uses the ledger; the 2026-03 support modules use the blackboard. Neither reads the other. That is the single most confusing thing in the whole system for a new reader, and it is not a design, it is a seam between two generations of code. The unused `BotTask` scaffolding should probably go.

#### E3. `ThreatMapManager` — coarse omniscient influence grid
- **Purpose:** per-cell friendly/enemy military value, economic value and exploration age. **World trait**, `world.yaml:283-286`, `CellSize: 8`, `UpdateInterval: 90` (5.4 s), `SpreadFactor: 0.3`.
- **Provenance:** **WW3MOD** (2026-03-21, 2 commits).
- **Consumers:** `ScoutBotModule` (`MarkExplored`), `GarrisonBotModule` (`PrioritizeExposed`), `HelicopterSquadBotModule` (`FindWeakestEnemyCell` — the frozen drop-site picker).
- ▶ **Assessment.** It is **omniscient** — it does not respect fog, unlike the newer `SightingThreatLayer` / `BeliefStore` (`world.yaml:336`). It is also a `float`-based grid (`SpreadFactor: 0.3f`), which sits awkwardly against the influence stack's zero-RNG / byte-identity invariants. Every consumer of it is a 2026-03-generation module. It reads as the first-generation spatial layer that the influence stack replaced but which was never retired.

---

### Group F — pure decision math (not modules)

28 files in the same folder are engine-free static classes holding the decision math, NUnit-pinned so they port to a future brain without the engine: `AdaptiveRoutingMath`, `AmmoEvacMath`, `BotEarlyGameMath`, `CaptureSupplyMath`, `CombatRetreatMath`, `CompositionNeedMath`, `ContinuousBombardMath`, `EchelonMath`, `EscortSizingMath`, `EvacDriveOffMath`, `FiresEconMath`, `FiresStandoffMath`, `ForceCompositionMath`, `ForwardStagingMath`, `FrontierStandoffMath`, `FrontlineAllocationMath`, `LateralSpreadMath`, `OpportunisticAdvanceMath`, `OrderArbitrationMath`, `PrepFireMath`, `RetreatDamperMath`, `SpawnFlowMath`, `SupplyDropMath`, `SupplyLogisticsMath`, `SupplyTruckHuntMath`, `TransportEmploymentMath`, plus the helpers `ActorNameCase`, `PoiReachability`. Also `BotModuleLogic/BaseBuilderQueueManager.cs` (inherited, drives the inert construction queues) and `BotModuleLogic/SupportPowerDecision.cs` (inherited, unused).

▶ **Assessment.** This factoring is the healthiest thing in the codebase and it is what makes the WW3MOD modules portable in a way the inherited ones are not. Note the contrast: not one inherited module has a `*Math` partner.

---

## 4. Fitness — the three concerns I would act on

**1. `BaseBuilderBotModule@normal` and the base-building config are a live, tunable-looking surface that cannot move anything.** Eight building types, all `~disabled`; `NewProductionCashThreshold`, `MinBaseRadius`, `PlaceDefenseTowardsEnemyChance` and the rest all dead. Its one live behaviour (`SetRallyPoint`) is unrelated to base building. This is the purest example of an RA-era module surviving the conversion because nobody wanted to delete a trait. The cost is not CPU — it is that the config file *lies* about what is tunable, which is exactly how the `EvacDangerThreshold` class of bug gets made. See §2.1, §3-B1.

**2. Two generations of code share the bot and do not talk to each other.** The 2026-03 support modules (`Scout`, `Garrison`, `SupplyFollower`, `AdaptiveProduction`, `HelicopterSquad`, `BotBlackboard`, `ThreatMapManager`) use the blackboard for claims and an omniscient float grid for space. The 2026-07 POI stack uses `PoiGoalGuard` for claims and the fog-legal influence/belief/control fields for space. **Neither registry reads the other**, and the older layer is where the shared `enable-ai-any` instances live — so the modules with the weakest intel model are also the ones with no `@stable` twin protecting the benchmark. The most concrete symptom is C3: `AdaptiveProductionBotModule` owns a correct fog-legal enemy scanner and gates it behind a last-write-wins, never-decaying scout counter.

**3. Both transport modules are supply-driven in a game whose transport problem is demand-shaped.** Neither ferry ever asks where a unit needs to go; each computes one destination per pass, delivers whoever happens to be within 14 cells of the beachhead, and releases them. Under the reinforcement model every unit is born at the map edge and walks to the SR, so the bubble sits where the units are — but it is a **one-shot**: past 14 cells from home a unit can never be lifted again for the rest of the match (`260808-transport-census.md` §0.4). Per-unit destinations exist but are private fields on `Axis` / `Garrison` with no accessor, and the shared ledger stores an objective string with **no position**. So this is a missing layer, not a wiring job.

**Honourable mentions** (real, smaller): `SquadManagerBotModule` is 565 lines plus a whole FSM directory serving two airframes per faction, with its ground, naval, base-hanging and fuzzy-logic machinery all unreachable (§3-B3). `EngineerRouteOpenBotModule` is fully built and has no target on any shipped map (§2.1). `GarrisonBotModule@defenses` garrisons civilian houses on both profiles while named after defences that cannot be built (§3-C2). `SupportPowerBotModule` is absent while `MSLO` exists.

---

## 5. Bug filed

One new defect found while writing this, logged (not fixed) in [`WORKSPACE/bugs/discovered.md`](../../WORKSPACE/bugs/discovered.md):

> **2026-08-09 [med] `AdaptiveProductionBotModule`'s counter-buy lane is gated on stale, never-decaying blackboard intel while its own fog-legal scanner sits unread behind the gate.**

Pre-existing defects referenced above and already on the record: the `EvacDangerThreshold` scale mismatch (`WORKSPACE/recon/260809-truck-loop-from-live-log.md:211-212`); `GoalGuardLedger.Release` keyed on the actor (`discovered.md`, 2026-08-09); the `GarrisonBotModule` frozen-truck claim leak (`discovered.md`, entry 22, **fixed**); the `UnloadCargo` order-name no-op (`discovered.md`, entry 80, **fixed**); the `AirUnitsTypes` case no-op (`discovered.md`, 2026-07-24, **fixed**).

---

## 6. Verification notes

- Provenance was established with `git log --diff-filter=A --format=%ad --date=short` per file, **not** from copyright headers — several WW3MOD-original files (`SupplyFollowerBotModule`, `AdaptiveProductionBotModule`, `HelicopterSquadBotModule`, `BotBlackboard`) carry the inherited OpenRA notice at line 3.
- Twin comparisons in §2.2 were done mechanically: extract every `\t\t<Key>: <value>` line from each block, drop `RequiresCondition`, sort, `comm`. Both directions were checked.
- The inert claims in §2.1 were each traced to the terminating condition in code, not inferred from the YAML.
- Line-number drift is real in this repo. The recon censuses this document synthesises were written at `9b39ebf1` / `8d0ff18b`, and `ai.yaml` plus six modules have changed by 3,548 lines since. **Every citation here was re-read at `4d583f2e`.** If you are reading this more than a few weeks out, re-grep before acting.
