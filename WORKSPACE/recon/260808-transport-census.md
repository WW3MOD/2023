# Transport census — what carries whom, where, and why every soldier walks

**Researched against `main` @ `8d0ff18b`** (`git status -sb`: `main...origin/main [ahead 37]`, `git rev-list --count HEAD..@{u}` = 0 ⇒ not behind upstream; tree clean apart from untracked scratch). Static analysis only — **no game runs, no autotests**. Every claim carries a `file:line` read at that SHA.

**What this document is.** The census that sizes a proposed *transport-pooling layer* — "pool demand, group as many soldiers as possible into each transport, move a SQUAD not a passenger." It proposes no design. Where a fact could not be established it says so.

**Sibling doc, read it first:** [`260807-order-source-census.md`](260807-order-source-census.md) — two order layers, per-call countdowns, YAML-declaration-order conflict resolution. This document situates transport inside that map and does not restate it. **Line-number drift:** that census cites `ai.yaml:949/983/1245/1265` for the transport blocks; `ai.yaml` has grown since `9b39ebf1` and those blocks are now at `:1053/:1087/:1349/:1369`. Its *claims* still hold; its transport line refs do not.

**Timestep** 60 ms ⇒ 16.667 ticks/s (`mods/ww3mod/mod.yaml:369-372`); `seconds = ticks × 0.06`.

---

## 0. Headline findings

1. **The premise "one passenger at a time" is FALSE for both real transport modules.** Both already pool. `MountedTransportBotModule` loads 2–5 per trip and *waits* for the minimum (`MountedTransportBotModule.cs:375-394`, `MinPassengersPerLoad: 2` / `MaxPassengersPerLoad: 5`, `ai.yaml:1062-1063`). `HelicopterSquadBotModule` loads 4–8 and waits (`TransportLoadMath.Decide` at `:1196`, `TransportMinInfantry: 4` `ai.yaml:1356`, `TransportMaxInfantry` C# default 8 `:72`). **Batching is not the missing piece.**
2. **The missing piece is DEMAND.** Neither module asks where any soldier needs to go. Both compute their own single destination per pass — the mounted one drops at "the thinnest cell of *our* frontline" (`MountedTransportBotModule.cs:653-698`), the heli at "the weakest *enemy* cell" or a risk-ranked POI (`:1044-1061`, `:1104-1161`). A pooled ride today is **supply-driven, not demand-driven**: it moves whoever is standing near the SR to wherever the module unilaterally decided, then releases them, and the offense stack promptly walks them somewhere else.
3. **Per-unit destinations are private.** The shared `PoiGoalGuard` ledger stores `{Objective:string, ExpiresAtTick, CommitCount}` and **no position** (`PoiGoalGuard.cs:41-51`). The destination-bearing state lives in `sealed class Axis` / `sealed class Garrison`, private nested classes with no accessor (`PoiOffensiveBotModule.cs:869-879`, `PoiGarrisonBotModule.cs:145-156`). **This makes pooling a new layer, not a wiring job** — with one qualification, §4.3.
4. **The pickup geometry is a 14-cell bubble around the own SR, and it is the reason the POI journeys the user cares about are structurally out of scope.** Both modules gate passengers to `ReserveZoneRadiusCells: 14` / `LiftReserveZoneRadiusCells: 14` of the own Supply Route (`MountedTransportBotModule.cs:562,573`; `HelicopterSquadBotModule.cs:1609`), plus a 6-cell corridor along the SR→drop lane on the mounted side (`ai.yaml:1074,1106`). **Once a soldier is more than 14 cells from home it can never be picked up again, by anything, for the rest of the match.** Under the reinforcement model every unit enters at the map edge and walks to the SR (`game-model.md:22-27`, `supply-route.md:9-10`) — so the bubble does sit exactly where the units are born. But it is a one-shot: the first leg out of the SR is the *only* leg a transport can ever serve.
5. **On `@stable` the helicopter lift is starved to zero by construction, and on both profiles it can fly at most one mission at a time.** `TransportMissionSlots` is set only on `@experimental` (`ai.yaml:1485`); with it at 0 the launcher falls through to `activeSquads.Count >= MaxActiveSquads` (3, `ai.yaml:1357`), a counter a transport mission never increments (`HelicopterSquadBotModule.cs:1002-1008`) — a permanent starve, documented in-repo at `architecture.md:327` and `ai.yaml:1468-1471`.
6. **Nothing models a rendezvous.** `ForwardStaging` is not it — it repositions idle *attack* helicopters, carries no passengers, and had never moved a single airframe until the `IsIdle` fix (§6). The only wait primitive that exists is "hold the carrier at its own position until N are aboard or a fixed timeout", and it has no re-issue: **a poached passenger is never re-ordered to board**, it just burns the slot until the 1500-tick timeout (§4.2).

---

## 1. Every path by which a bot unit gets into a transport

Five, and only two of them are ferries. For each: passenger selector / carrier selector / **destination** selector / batching.

### 1.1 `MountedTransportBotModule` — the frontline shuttle (BATCHES 2–5)

`ai.yaml:1053` (`@poi`, `enable-ai-stable`) and `:1087` (`@experimental`). `ScanInterval: 50` = 3.0 s (`:1059`, `:1089`), randomised at enable (`MountedTransportBotModule.cs:302`).

| | |
|---|---|
| **Passenger selector** | `TryAssignNewTasks` `:563-576`. Owned, alive, in-world, actor name in the 20-entry `PassengerTypes` allowlist (`ai.yaml:1061`/`:1091`), has `PassengerInfo`, not reserved by one of our own tasks, not reserved by the heli twin (`:567`), not ledger-committed (`:571`, inert unless `CommitPassengers`), **and** within 14 cells of the own SR **or** within 6 cells of the SR→drop lane (`:573-575`, `MountedTransportMath.InCorridor` `:774`). **Deliberately NOT `IsIdle`** — `:531-535`. |
| **Carrier selector** | `:500-521`. Owned, alive, in-world, name in `CarrierTypes: bradley, bmp2, m113` (`ai.yaml:1060`/`:1090`), has `Cargo`, is **empty**, not already in a task. **Deliberately NOT `IsIdle`** — PITFALL comment `:494-499`. |
| **Destination** | `PickDropOffCell` `:653-698`. Scans `influenceMap.GetFrontline(player)` and picks the frontline cell with the **lowest friendly influence** — the thinnest gap in *our own* line (`:685`). Enemy concentration is explicitly not considered (`:681-684`). With no frontline (pre-contact) it falls back to `PreContactStagingCell` (`:703-722`) = a 50 % lerp from the SR toward `poiMap.GetOffensiveTargets(player)[0]` — `DeliverBeforeContact: true`, `PreContactStagingPct: 50` on **both** twins (`ai.yaml:1071-1072`, `:1098-1099`). Then `ApplyStandoff` (`:729-762`) walks the cell back toward the SR out of believed `DangerFieldLayer.GroundDanger` (`BelievedDangerStandoff: true` on both, `ai.yaml:1075`, `:1110`). **One shared drop cell per pass, for every carrier in that pass** (`:584-590`). |
| **Batching** | **Yes.** `capacity = min(MaxPassengersPerLoad, cargoInfo.MaxWeight)` (`:600`); `toLoad = availablePassengers.OrderBy(dist).Take(capacity)` (`:605-608`); **`if (toLoad.Count < MinPassengersPerLoad) continue;`** (`:609-610`) — it refuses to start a single-passenger run. Then it *waits*: `Loading` holds until `cargo.PassengerCount >= MinPassengersPerLoad` or `LoadingTimeoutTicks: 1500` (90 s) elapses, at which point it delivers a partial load or aborts empty (`:372-396`). Loaded passengers are removed from the pool so the next carrier in the same pass takes different soldiers (`:639-640`), and the loop breaks when fewer than the minimum remain (`:645-646`). |

Per-carrier FSM `Loading → Delivering → Unloading → Returning` (`:144`, `:370-478`), advanced **only on a scan** — arrival, unload-completion and return-completion are all polled (`:400`, `:431`, `:468`).

### 1.2 `HelicopterSquadBotModule` lift — the air shuttle (BATCHES 4–8)

`ai.yaml:1349` (`@stable`) / `:1369` (`@experimental`). Launch attempt every `TransportInterval: 600` = **36 s** (`ai.yaml:1355`, `:1382`; countdown at `HelicopterSquadBotModule.cs:540-544`).

| | |
|---|---|
| **Passenger selector** | `IsLiftCandidate` `:1582-1610`. Owned/alive/in-world; has `WithInfantryBodyInfo`; `cargo.Info.Types` overlaps the actor's target types; role `MainBattle` per `UnitRoleResolver` (`RestrictLiftToLineInfantry`, C# default **true** `:81`, unset in YAML) — **fails closed** if no resolver; not reserved by one of our tasks; not reserved by the mounted twin (`:1603`); not ledger-committed (`:1606`); and within `LiftReserveZoneRadiusCells` (C# default **14**, `:91`, unset in YAML) of `LiftHomeCell()` = own SR, falling back to `player.HomeLocation` (`:1618`). |
| **Carrier selector** | `:1014-1029`. From `idleHelicopters` (a pool-membership list, not an `IsIdle` scan): `AIHelicopterRole.Transport` **and** `IsReadyForMission(h)` — which enforces `ReEngageHealthPercent`, shipped **90** on tran/halo, with no AI repair host, so **one chip of damage benches an airframe permanently** (`architecture.md:327`). Must have `Cargo`. |
| **Destination** | `:1044-1064`. Frozen path: `threatMap.FindWeakestEnemyCell(player)` if its threat `< 50` — an **omniscient** read, the one drop-site picker that is not fog-legal (`DISCOVERIES.md:372`). `@experimental` swaps in `PickRiskWeightedDropZone` (`:1104-1161`, `RiskWeightedDropSite: true` `ai.yaml:1461`), ranking {weak cell} ∪ {top 6 believed offensive POIs} by believed control depth / danger / distance-from-own-SR. **If no drop zone resolves, the whole mission is abandoned** (`:1063-1064`). |
| **Batching** | **Yes.** `LiftLoadCap = TransportEmploymentMath.LoadCap(TransportMaxInfantry, cargo.MaxWeight, TransportMinInfantry)` (`:1646-1649`) — `.Take(cap)` at `:1038`; **`if (infantry.Count < TransportMinInfantry) return;`** (`:1041-1042`). Then a two-phase load: only `EnterTransport` orders go out (`:1074-1075`), and `AdvanceTransportTasks` (`:1168-1221`) dispatches the delivery **only once `cargo.PassengerCount` confirms embarkation**, delivers a partial load on timeout (`TransportLoadTimeoutTicks` C# default 1500 `:100`), or aborts empty and returns the airframe to the pool. |

**Concurrency cap:** `ActiveTransportMissions = transportTasks.Count + transportsAwaitingUnload.Count` (`:994`) measured against `TransportMissionSlots` = **1** on `@experimental` (`ai.yaml:1485`) and **0** on `@stable` — where 0 means "fall through to the shared `MaxActiveSquads` gate the path never increments", i.e. a permanent starve (`:1002-1008`).

### 1.3 The capture ferry — a DIRECTED, single-passenger ride (does NOT batch, by design)

`CaptureCoordinatorBotModule.TryFerryCapture` (`:1069-1082`) → `MountedTransportBotModule.TryReserveCaptureFerry` (`:228-284`). This is what **`test-tecn-ride`** exercises.

- **Trigger:** event-driven, not on any timer. Fires inside `IssueCaptureOrder` (`:1042-1048`) when `UseTransportForDistantCaptures` is set (**true on both** `.tecn` twins — `ai.yaml:159` and `:1563`) and the capture target is ≥ `TransportCaptureMinDistanceCells: 12` from the capturer (`:1070-1071`).
- **Passenger:** exactly the one TECN being dispatched. **Carrier:** nearest owned, empty, task-free `CarrierTypes` actor to the *capturer* (`:242-260`) — no SR-bubble gate, unlike every other path.
- **Destination: `target.Location`** — the capture target itself (`:274`). This is the **only** transport path in the codebase whose destination is a real objective handed in from outside, and it bypasses `PickDropOffCell` entirely (`:227` comment), which is why it works pre-contact.
- **Batching: none, correctly.** `minPax` is forced to 1 for a capture ferry (`:375`); `MinPassengersPerLoad` does not apply. On unload the module hands the TECN back its `CaptureActor` (`:435-444`).

### 1.4 `GarrisonBotModule@defenses` — genuinely one-at-a-time

`ai.yaml:731`, `RequiresCondition: enable-ai-any` (**one shared instance, runs for both bots**), `ScanInterval: 200` = 12 s (`:733`).

Buildings are `Cargo` carriers (GTWR 6, PBOX 4, HBOX 4, `^CivBuilding` 10 — §2). Selector: owned `Mobile`, **`IsIdle`**, garrison-eligible, not blackboard-claimed, not ledger-committed on `@experimental` (`GarrisonBotModule.cs:161-172`). Destination = the building. **Batching: none** — `.FirstOrDefault()` picks exactly one infantryman per building per pass (`:196-200`), and the whole pass is capped at `MaxOrdersPerTick: 2` (`ai.yaml:735`). Passengers stay inside; this is occupation, not transit.

### 1.5 Human/scripted — `Test.IssueEnterTransport`

Autotest binding only (`test-pips-zoom.lua:52-54` loads 4 into a bradley; `test-spread-cargo-no-enter.lua:40`). No bot involvement.

### 1.6 What does NOT exist

No path lifts a soldier that is already **on the line**. No path takes a request from `PoiOffensiveBotModule`, `PoiGarrisonBotModule` or `LayeredDefenceBotModule` — the three modules that actually decide where infantry should be. The capture ferry (§1.3) is the sole precedent for a module *asking* for a ride, and it exists because capture destinations are far and TECNs are a consumable.

---

## 2. Capacity — the mechanical facts

**Engine defaults** (`engine/OpenRA.Mods.Common/Traits/Cargo.cs`, `Passenger.cs`): `Cargo.MaxWeight = 0` (`:30`), `Cargo.Types = {}` (`:33`), `Passenger.Weight = 1` (`:29`), `Passenger.CargoType = null` (`:24`). **An empty `Types:` means NOTHING boards, not everything** — the gate is `ci.Types.Contains(Info.CargoType)` (`Passenger.cs:120`). Space test is `totalWeight + reservedWeight + weight <= MaxWeight` (`Cargo.cs:381`). **`PipCount` is not a `Cargo` field** — it is `WithCargoPipsDecoration.PipCount` (default −1, falling back to `MaxWeight`); cosmetic only.

**Every infantry weighs 1.** All infantry inherit `^Infantry` → `Passenger: CargoType: Infantry` (`infantry.yaml:85`); no infantry actor anywhere sets `Weight:`. **No `Cargo:` in the mod declares `Types: Vehicle`**, so the whole vehicle weight class (10/15/20/30) is dead capacity math — no vehicle can ride in anything. ⇒ **`MaxWeight` is a literal seat count.**

| Carrier | file:line | Seats | Air/Ground | Used as an AI carrier? |
|---|---|---|---|---|
| **tran** (Chinook) | `aircraft-america.yaml:72` | **36** | Air | heli-lift only, capped to 8 |
| **halo** (Mi-26) | `aircraft-russia.yaml:48` | **36** | Air | heli-lift only, capped to 8 |
| **m113** | `vehicles-america.yaml:265` | **12** | Ground | yes — **capped to 5, 7 seats wasted** |
| strykershorad | `vehicles-america.yaml:974` | 9 | Ground | **no** |
| humvee | `vehicles-america.yaml:139` | 8 | Ground | **no** (scout only, `ai.yaml:716`) |
| btr | `vehicles-russia.yaml:103` | 8 | Ground | **no** (scout only, `ai.yaml:724`) |
| hind | `aircraft-russia.yaml:252` | 8 | Air | **no** |
| **bmp2** | `vehicles-russia.yaml:262` | **7** | Ground | yes — capped to 5 |
| **bradley** | `vehicles-america.yaml:418` | **6** | Ground | yes — capped to 5 |
| littlebird | `aircraft-america.yaml:222` | 4 | Air | **no** (scout) |
| GTWR / PBOX / HBOX / `^CivBuilding` | `structures-defenses.yaml:116/209/297`, `civilian.yaml:52` | 6 / 4 / 4 / 10 | static | garrison only (§1.4) |
| BADR, `^SummonerDummy` | `aircraft.yaml:399`, `defaults.yaml:907` | 16 / 10 | Air | **unreachable** — empty `Types:` |

**What "group as many as possible" can mean today, per trip:** ground 5 (config cap, `ai.yaml:1063`/`:1093`), air 8 (`TransportMaxInfantry` C# default, `HelicopterSquadBotModule.cs:72`). **What the hulls physically allow:** ground 12, air 36. **Unaddressed lift the AI already owns or could own:** humvee 8 + btr 8 + strykershorad 9 + hind 8 + littlebird 4 = **37 seats never used as transport**, plus 7 wasted on every m113 trip and 28 on every heavy-lifter trip.

`PassengerTypes` (`ai.yaml:1061`/`:1091`) names 20 infantry and omits `e1.*`, `e6.*`, `sf.*`, `tecn.*`, `dr.*`, `pilot.*` and the `*R1` rocket variants — those are never picked up by the mounted path. (The heli path uses a role test instead of an allowlist, so its pool is a strict superset — which is exactly why the two modules need the mutual reservation seams of §4.2.)

*(§2 numbers were gathered by a delegated YAML sweep; I re-read and confirmed bradley 6, bmp2 7, m113 12, tran 36, halo 36 and the `Passenger.cs:120` type gate directly.)*

---

## 3. Is there any notion of "where a soldier wants to go"?

**No. Say it plainly: strategic destinations are private to the module that issues the move.** This is the finding that makes pooling a new layer.

### 3.1 What the shared ledger does and does not carry

`GoalGuardLedger<TKey>.Commitment` is `{ string Objective; int ExpiresAtTick; int CommitCount; }` (`PoiGoalGuard.cs:41-51`). **There is no position field.** The ledger answers "is this unit claimed, by what named objective, until when" — never "where is it going".

### 3.2 Where the destinations actually live

| Module | Destination state | Visibility |
|---|---|---|
| `PoiOffensiveBotModule` | `Axis.TargetCell` / `TargetPos` / `OrderedCell` / `OrderedVia` (`:872-878`), plus `stagedCells` (`Dictionary<Actor,CPos>`, per-unit last-ordered cell, `:2265-2270`) | **`sealed class Axis` is a private nested class; no accessor** |
| `PoiGarrisonBotModule` | `Garrison.PoiCell` / `OrderedCell` (`:147-152`) | **private nested `sealed class Garrison`** |
| `LayeredDefenceBotModule` | slot `CPos`, private; `assignedAtTick` `Dictionary<Actor,int>` (`:181`) | private |
| `MountedTransportBotModule` | `CarrierTask.DropOff` / `.Return` (`:150-151`) | **private nested `sealed class CarrierTask`** |
| `HelicopterSquadBotModule` | `TransportLoadTask.DropZone` (`:455-460`) | private nested |
| `CohesionSlotMemory` (per-unit trait) | **`public CPos? AssignedSlot` (`:115`), `public CPos? OrderPoint` (`:119`)** | **PUBLIC** |

`CohesionSlotMemory.AssignedSlot` is the **only public per-unit destination accessor in the codebase**. It is a *formation slot* — a local, tactical position within a group (`:227` `QueueActivity(new Move(self, assignedSlot))`) — not a mission objective, and it is only meaningful once the unit is already where the group is. It is not the demand signal a pooling layer needs.

### 3.3 The one exception, and why it matters

Objective keys are namespaced strings, and **one of them encodes a map cell**: `LayeredDefenceBotModule.LineObjectiveKey(CPos slot) => "defend-line:" + slot.X + "," + slot.Y` (`:197`). The rest encode an actor or POI id — `offense:<targetId>`, `defend:<poiId>` (`PoiGarrisonBotModule.cs:510`), `capture:<actorId>` (`CaptureCoordinatorBotModule.cs:1088`), `garrison:<buildingId>` (`GarrisonBotModule.cs:77`), `ambush:<anchorId>` (`LaneAmbushBotModule.cs:591`), `bridge-repair:/bridge-screen:<hutId>` (`EngineerRouteOpenBotModule.cs:174-175`), `transport:<carrierId>`, `tacpos:<actorID>`.

**There is exactly one precedent for reading the payload rather than the prefix:** `CaptureCoordinatorBotModule.BuildInFlightCaptureTargetIds` (`:1021-1038`) parses `capture:<id>` back to a target ActorID via `TryParseCaptureTargetId`. Every *other* consumer of `TryGetObjective` does a bare `StartsWith` prefix test for attribution only (`LaneAmbushBotModule.cs:393`, `PoiGarrisonBotModule.cs:450`, `PoiOffensiveBotModule.cs:2684`/`:3517`, `Squads/States/StateBase.cs:168`).

So: the ledger is a **low-bandwidth destination channel that already exists and is already used as one, once**. `defend-line:X,Y` yields a cell directly; `offense:`/`defend:`/`capture:` yield an id resolvable through `PoiMap` (`ScoredPoi` carries `Location` and `CenterPosition`, `PoiMap.cs:61-62`) or a world-actor lookup. **INFERRED, not verified:** that this is sufficient bandwidth for a pooling matcher. It gives a destination but not a deadline, a priority, or a "this unit is *currently walking* there and would benefit from a ride" flag — and a unit that is not committed at all (the common case for fresh reinforcements at the SR, which is precisely the pooling layer's input) has no ledger entry and therefore no destination at all.

---

## 4. What a pooling layer plugs into — and what fights it

### 4.1 Where it sits in the two-layer order map

Transport lives entirely in the **module/order layer** (`bot.QueueOrder` → `ModularBot.QueueOrder` → `world.IssueOrder`, ≥2 ticks of latency, 1/5-per-tick drain — `260807-order-source-census.md` §1.1, §3.2). Every transport order is an `Order`; none of it bypasses the funnel. That is good news: an order-layer pooling scheduler *would* see all of it.

It does **not** protect it from the unit-level layer (`260807` §1.4). `StancePositioningExecutor` (`defaults.yaml:27`, every 30 ticks = 1.8 s, on every `^Combatant` under `@experimental` **and every human-owned combatant**) queues `Move` activities directly (`:414`) and writes `tacpos:` to the ledger while **never reading it** (`:643`). `AutoSeekSupplies` (`infantry.yaml:221`, every 40 ticks = 2.4 s, on **every soldier**) queues `SeekSuppliesAndReturn` (`:112`). Neither produces an `Order`. A soldier walking to a rendezvous is exposed to both. **NEEDS A LIVE RUN** to establish whether either actually interrupts a boarding walk in practice — `RideTransport` is `CurrentActivity` throughout, so `INotifyIdle`-driven traits should not fire, but I did not trace the `TickIdle` predicates far enough to assert it.

### 4.2 Who can claim a soldier away, and when

**A passenger *aboard* is safe. This is verified and structural.** `RideTransport.OnEnterComplete` calls `enterCargo.Load(...)` then **`w.Remove(self)`** (`Activities/RideTransport.cs:78-86`). A removed actor is not in `world.Actors`, and every free-pool scan additionally tests `IsInWorld` (`PoiOffensiveBotModule.IsEligibleCombatUnit:2282`). So no module can order a loaded passenger. `IsInWorld` is also exactly the "did not make it aboard" test the stand-down sweeps rely on (`HelicopterSquadBotModule.cs:1233-1243`).

**The boarding WALK is the entire hazard window**, and it is up to 90 s wide (`LoadingTimeoutTicks: 1500` / `TransportLoadTimeoutTicks` default 1500). Three defences exist, and none is complete:

| Defence | Written by | Read by | Gap |
|---|---|---|---|
| `PoiGoalGuard` ledger `transport:<carrierId>` | mounted only under `CommitPassengers` (**`@experimental` only**, `ai.yaml:1118`; absent from `@poi`); heli only under `CommitTransportPassengers` (**`@experimental` only**, `ai.yaml:1446`) | every POI-stack free pool | **On `@stable` NEITHER transport module writes it** ⇒ the ledger cannot mediate at all. `MountedTransportBotModule` does not even *resolve* `goalGuard` unless the flag is set (`:313-314`) |
| `MountedTransportBotModule.IsPassengerReserved` (`:182-188`) | — | `HelicopterSquadBotModule:1603`, `LayeredDefenceBotModule:393` | **not read by** `PoiOffensiveBotModule`, `PoiGarrisonBotModule`, `LaneAmbushBotModule`, `CaptureCoordinatorBotModule`, `EngineerRouteOpenBotModule`, `GarrisonBotModule` |
| `HelicopterSquadBotModule.IsPassengerReserved` (`:1635-1642`) | — | `MountedTransportBotModule:567` | same gap |

`PoiOffensiveBotModule.BuildFreePool` (`:1908-1918`) filters on **axis-claim and ledger only**. It does not consult either seam. Its `AttackMove` is non-queued (`:2215` etc.), which hard-cancels `RideTransport` (`Actor.cs:381-387`; `Activity.Cancel` nulls `NextActivity`). Cadence 100 ticks = 6.0 s. **Consequence on `@stable`: offense re-evaluates ~15 times inside one mounted loading window and will take a boarding soldier the first time it wants one.**

**And the poach is not recovered.** `MountedTransportBotModule.AdvanceTask` (`:372-396`) only polls `cargo.PassengerCount`; it never re-issues `EnterTransport` to a passenger that was pulled away. The stolen soldier stays in `ReservedPassengers` — so it is excluded from *other* carriers' pools (`:539-540`) while contributing nothing — until the 1500-tick timeout fires. **One poach costs a carrier slot for up to 90 s.** The heli path is marginally better: it aborts and issues `Stop` to every straggler (`StandDownStragglers`, `:1235-1244`) — necessary because `Cargo.ReleaseLock` only fires at `reservedWeight == 0` (`Cargo.cs:351-353`), so an unreleased reservation pins the airframe's pickup lock forever.

**Conflict order is decided by YAML declaration order in `ai.yaml`, documented nowhere in code** (`260807` §3.3). `MountedTransportBotModule@poi/@experimental` (`:1053`/`:1087`) is declared **after** `PoiOffensiveBotModule` (`:235`) and `LayeredDefenceBotModule` (`:869`-ish) but **before** `HelicopterSquadBotModule` (`:1349`/`:1369`). A pooling layer inherits that ordering as a load-bearing, invisible dependency.

### 4.3 Cadence: withholding corrupts state

Every transport cadence is a `--countdown` decremented **per call**, not a tick stamp (`260807` §6.3). Specific to transport:

- `MountedTransportBotModule` is a **polled** 4-state FSM (`:370-478`): arrival at the drop (`:400`), unload completion (`:431`) and return completion (`:468`) are detected **only on a scan**. Skip a scan and a carrier that reached its drop never unloads. `LoadingTimeoutTicks` is measured in world ticks but *observed* only on a scan, so the effective timeout quantises to the attention interval.
- `HelicopterSquadBotModule` runs **five** countdowns in one tick (`:503-551`), none staggered, plus `EvaluateIdleHelicopters` unconditionally every tick (`:550`). `PruneSquads` must run on the 5-tick branch — a squad state tick reaching a Disposed member **throws** (`:742-748`). `idleTicks` counts consecutive *calls* of a function documented as deliberately running every tick so its gate counts game ticks.

### 4.4 The determinism contract

`influence-stack.md:101-107`: zero `SharedRandom`/`LocalRandom` in the influence stack; deterministic self-stagger; byte-identity when flags are off. Transport currently *does* draw RNG at enable (`MountedTransportBotModule.cs:302`; heli/others likewise) — consistent, not broken, but a pooling layer subsuming these cadences must preserve the draw order or replace it with deterministic offsets. Do **not** gate on `InfluenceStack.Participates`; since 2026-08-02 it returns true for `experimental`, `stable` *and* humans.

---

## 5. What already went wrong here

`auto/transport-lift` (`3e59fed1`, plus `557969eb`, `bc536d19`, `f0f888e5`) made helicopter lift reachable **for the first time ever**. The relevant history:

**`Actor.IsIdle` is `CurrentActivity == null` (`Actor.cs:75`), and `Actor.Tick` runs a newly-queued activity inside the SAME tick** ("to avoid an 'empty' null tick", `Actor.cs:290-299`) — so the null gap is never observable for any actor whose traits queue on idle.

- **For an airframe it is DEAD CODE, proven not observed.** `Aircraft.OnBecomingIdle` queues `FlyIdle` (`Aircraft.cs:936`), which exits only on `ForceLanding`/cancel/queued-successor (`Activities/Air/FlyIdle.cs:40-44`). A hovering heli is never idle at *any* tick a bot can sample. Casualties: `EvaluateIdleTransport`'s `!h.IsIdle` reset `idleTicks` every tick so `TransportIdleEvacuateTicks: 900` was unreachable; **`ForwardStaging` had never staged a single airframe on either profile despite shipping tuned as `true`**; the attack-heli `EvacuateWhenIdle` window was equally inert; `EnsureTransportsUnload`'s re-issue never fired. Replacement is `AIUtils.IsUnoccupiedAirframe` (`AIUtils.cs:45-51`), used at `HelicopterSquadBotModule.cs:1555` and `UnitBuilderBotModule.cs:567`.
- **`IsReadyForMission`'s `!h.IsIdle` (`:1283`) is CORRECT and was deliberately left alone** — it is a fast path around the null case; the decision is the `Resupply` type check inside. The one site where the "obvious" fix is churn.
- **For ground infantry it is NOT dead code — it is unsatisfiable in practice.** `Mobile.OnBecomingIdle` queues nothing in the common case (`Mobile.cs:923-934`), so a soldier at the SR genuinely *is* idle. It fails for a different reason: infantry pushed forward engage through `AutoTarget` and are never idle again (`LayeredDefenceBotModule.cs:86-89` PITFALL, `carriers-candidate=0`). Requiring **four simultaneously-idle** soldiers on the one tick a 600-tick launcher samples was never satisfied — **not one heli lift had ever launched, on any profile.** Replaced by availability-for-tasking: reserve zone + ledger + reservations + role.
- **`IsIdle` was silently doing a THIRD job.** A soldier walking to board carries `RideTransport`, so the old filter excluded another transport's in-flight load *by accident*. Removing it without adding explicit `ReservedPassengers()` would have let a second airframe pick a load already boarding.

**The trap this hands a pooling layer, stated directly: "waiting to fill up" and "idle" are indistinguishable through `IsIdle`, and always will be.** A carrier holding station for passengers is either `FlyIdle` (air — never idle) or `Stop`-parked (ground — idle, and therefore claimable-looking). A soldier walking to a rendezvous is *not* idle and will be skipped by every `IsIdle`-filtered consumer (which self-protects) — but a soldier *standing at* a rendezvous **is** idle and is visible to `LayeredDefenceBotModule` (`:351`), `GarrisonBotModule` (`:163`), `CaptureCoordinatorBotModule`'s escort sweep (any armed idle unit within 40 cells) and `EngineerRouteOpenBotModule`'s screen. A rendezvous therefore manufactures exactly the state that is maximally poachable.

**Second-order hazard, already recorded:** waking a dormant path makes the whole path new code, not just the diff. `.Take(cargo.Info.MaxWeight)` was pre-existing and harmless while unreachable; live, it ordered **36** soldiers aboard for a mission dispatching at 4, and the surplus reservations pinned the airframe's pickup lock while the stragglers chased a departing heli. Any pooling layer that widens the load will re-enter this territory.

**Money-pump coupling, still worth checking:** `TransportMissionSlots: 1` + `EvacuateIdleTransports: true` + `tran/halo` UnitLimit 2 means a second transport has no free slot, never returns `Employ`, evacuates at ~54 s and is rebought. The `ShouldBuyTransport` half of this was fixed (`UnitBuilderBotModule.cs:567` now uses `IsUnoccupiedAirframe`); the slot arithmetic is unchanged. **NEEDS A LIVE RUN.**

---

## 6. The staging problem

**`ForwardStaging` is not a rendezvous and carries no passengers.** `HelicopterSquadBotModule.ForwardStaging` (`:233`, `ai.yaml:1361`/`:1395`) pushes **idle attack helicopters** that are still within `ForwardStagingMaxDistanceCells: 8` of the own SR out to `ForwardStagingPct: 40` of the SR→top-offensive-POI vector (`:670-722`, `ForwardStagingCell` `:726-740`). It is explicitly the heli twin of `MountedTransportBotModule.DeliverBeforeContact` (`:231`). It moved nothing at all until the `IsIdle` fix (§5). The similarly-named `ForwardStagingEnabled: true` at `ai.yaml:538` is a **different** thing — `PoiOffensiveBotModule`'s free-pool forward staging anchor (`ResolveStagingAnchor`, `:1926`).

**What "wait until N passengers, or until a deadline" looks like today.** Both modules implement it, identically in shape and only at the carrier's own position:

- Mounted: `Loading` holds while `cargo.PassengerCount < MinPassengersPerLoad`; at `LoadingTimeoutTicks: 1500` it launches a partial load if anyone boarded, else abandons the task (`MountedTransportBotModule.cs:372-396`). The carrier is parked by an explicit `Stop` order at reservation time (`:617`) so `AutoTarget` cannot hold it in an `Attack` activity and deny passengers a stationary entry frame (`:612-616`).
- Air: the same decision is a pure function, `TransportLoadMath.Decide(aboard, TransportMinInfantry, ticksLoading, TransportLoadTimeoutTicks)` → `Dispatch | Abort | Wait` (`HelicopterSquadBotModule.cs:1196`).

**So the wait primitive exists. What does not exist is a MEETING POINT.** In both cases the rendezvous is trivially the carrier's current cell, and the carrier does not move to meet anyone: passengers walk to it. There is no code anywhere that moves a carrier to a computed assembly cell and holds it there.

**The deadlock shape to avoid is already half-present.** The current design avoids it only because the carrier's wait is unconditional and time-bounded — it parks and waits, passengers walk in, timeout resolves. Introduce a *computed* rendezvous and both sides acquire a precondition, and the two known failure modes compose:

1. `MountedTransportBotModule` **never re-issues `EnterTransport`** after a poach (§4.2), so a passenger pulled off the walk never returns, while its `ReservedPassengers` entry keeps blocking other carriers.
2. A `Cargo` reservation is released only at `reservedWeight == 0` (`Cargo.cs:351-353`), and `LockForPickup` **cancels the carrier's own activity** (`:333-349`). A carrier that has reserved space for soldiers who will never arrive is locked out of loading anything else until every straggler's reservation is unwound — which only happens via `Stop`/`Cancel` → `RideTransport.Cancel` → `Passenger.Unreserve` (`RideTransport.cs:93-98`).

Combined: carrier waits for passengers who were poached and are never re-ordered; the carrier's own lock prevents it taking a replacement load; the timeout is the only exit. That is the deadlock, and it is a 90-second one today.

---

## 7. Test coverage

Three transport-adjacent autotests exist; **none asserts batching.** (Delegated sweep of `tools/autotest/scenarios/`; I did not re-read the scenario files myself.)

- **`test-tecn-ride`** — the only bot-driven one. One `tecn.america`, one `bradley`, one neutral `oilb`, `Bot: experimental`, `DefaultCash: 0`. Asserts a four-stage latch: carrier alive → `Carrier.HasPassengers` → within 6 cells of the derrick → `not HasPassengers` → derrick owned by the bot (`test-tecn-ride.lua:33-58`). **`HasPassengers` is a boolean; there is no count.** The map places one passenger, so batching is untestable there by construction. Status: expected GREEN but **never run since it was hardened** (`WORKSPACE/bugs/discovered.md:65`).
- **`test-spread-cargo-no-enter`** — a *negative* test: asserts three infantry must **NOT** end up in a BMP after a group scatter. Any pooling layer reusing the spread/waypoint aggregation path must keep this green.
- **`test-pips-zoom`** — loads 4 into a bradley and gates on `Transport.PassengerCount >= 4`, but the verdict is about rendering. It is the only place a multi-passenger load is exercised, and `PassengerCount` is the accessor a pooling test would use.

Nothing covers: the generic frontline delivery path, the pickup corridor, multi-trip behaviour, `MinPassengersPerLoad`, reservation contention between two would-be passengers, or the heli lift end-to-end.

---

## 8. Judgement

### 8.1 The smallest thing that makes bots move soldiers in groups

**They already do.** The correct framing of the smallest change is therefore *"make the existing pooling fire more often and fill the hulls"*, and it is **YAML-only, ~6 lines, no C#**:

1. `MaxPassengersPerLoad: 5 → 12` on both twins (`ai.yaml:1063`, `:1093`) — the cap, not the hull, is what limits an m113 to 5 of its 12 seats. **2 lines.**
2. `TransportMissionSlots: 1` on `HelicopterSquadBotModule@stable` (`ai.yaml:1349` block) — un-starves lift on the benchmark profile, where it is currently structurally impossible. **1 line.** *(Ship with `EvacuateIdleTransports` per the coupling note at `ai.yaml:1480-1484`, or don't ship it — read that note first.)*
3. Optionally add `humvee`, `btr` to `CarrierTypes` (`:1060`, `:1090`) — +16 seats of hardware the bot already buys and parks. **2 lines.** Trade-off: they are the scout types (`ai.yaml:716`, `:724`), and `strykershorad` additionally trips the `!IsTroopCarrier` exclusion, so this interacts with `FoldShortRangeAdIntoLine` (`ai.yaml:570`).

Cost: **under an hour**, plus one measured run. It buys more soldiers per trip and more trips. It does **not** address the user's actual complaint, because it does not change *where* the transport goes.

### 8.2 The honest full cost of the pooling layer the user described

Demand-driven pooling — soldiers converge, transport waits, departs, delivers to where each soldier was actually needed — is **a new subsystem, not a wiring job**, because §3 established that no per-unit strategic destination is published anywhere. Six pieces, none optional:

1. **A demand-publication seam.** Either a new public API on `PoiOffensiveBotModule`/`PoiGarrisonBotModule`/`LayeredDefenceBotModule` exposing per-unit target cells, or an extension of the ledger objective string into a resolvable destination (precedent: `TryParseCaptureTargetId`, `CaptureCoordinatorBotModule.cs:1021-1038`; `defend-line:X,Y` already carries a cell). The ledger route is cheaper and lower-risk, but it does **not** cover the pooling layer's main input — fresh reinforcements at the SR are uncommitted and therefore have no destination at all. That gap has to be closed on the consumer side, i.e. inside `PoiOffensiveBotModule`, the largest and most regression-prone file in the AI.
2. **A matcher** — group destinations into clusters, pick carrier ⇄ cluster ⇄ rendezvous. Pure math, NUnit-pinnable, integer-only, zero RNG. The cheap part.
3. **A rendezvous executor** — move the carrier to an assembly cell and hold it; move passengers to it. New behaviour: nothing today moves a carrier to meet anyone.
4. **Claim safety across the boarding walk** — the current three-way defence (§4.2) must become one mechanism honoured by *all* free-pool consumers, and it must survive on `@stable`, where neither transport module writes the ledger today. Realistically: give both transport modules unconditional ledger writes, and add re-issue-on-poach to `MountedTransportBotModule.AdvanceTask`.
5. **Deadlock protection** — bounded waits, mandatory straggler stand-down on every exit (the heli side has it, the mounted side does not), and `Cargo` lock hygiene (`Cargo.cs:333-355`).
6. **Determinism + byte-identity** — deterministic stagger, default-off flags on the shared trait classes, `@stable` unchanged until a measured promotion (`architecture.md:373-375`).

Plus, non-negotiable given §7: **at least two new autotest scenarios** (a multi-passenger bot-driven pooling test, and a poach-contention test), and a rerun of `test-spread-cargo-no-enter`, which asserts the *opposite* of pooling.

**Estimate: 2–4 working sessions of implementation across 4–6 files** (`MountedTransportBotModule.cs`, `HelicopterSquadBotModule.cs`, one new math class, `PoiOffensiveBotModule.cs`, `ai.yaml`, tests), **plus adversarial review and measured runs.** The history in §5 is the calibration: the last two transport waves each needed a full adversarial round that found blocking defects *not in the diff*, and this change wakes more dormant surface than either.

### 8.3 The single hardest part

Not the matcher, and not the plumbing. **It is that a pooled ride is a multi-unit commitment with a rendezvous, and this codebase has no way to express "hold these N units here until the others arrive."**

Every hold that exists is a per-call countdown on a single actor, and the only wait primitive — "carrier parks, passengers walk in, 90-second timeout" — works today *only because the carrier is already where the passengers are*. Move the meeting point and three separate mechanisms turn against it at once: a soldier standing at a rendezvous is `IsIdle` and therefore maximally visible to four other consumers (§5); a poached passenger is never re-ordered to board and silently burns a slot for 90 s (§4.2); and its unreleased `Cargo` reservation pins the carrier's pickup lock so it cannot even take a replacement (§6).

**The hard requirement underneath all three: a claim that means "committed to a transport mission" and is honoured by every free-pool consumer on both profiles — which today does not exist on `@stable` at all, and on `@experimental` is split across a ledger flag and two bespoke `IsPassengerReserved` seams that six of the eight free-pool consumers never read.**

---

## 9. What I could not establish

- **Whether the mounted shuttle actually fires in a live match.** Every config gate is open on both twins (`DeliverBeforeContact`, `UnloadOnArrival`, `PickupCorridorCells`, `BelievedDangerStandoff` — `ai.yaml:1071-1078`, `:1098-1113`), so it *should*. The `[exp-transport]` debug lines at `MountedTransportBotModule.cs:519,523,578` would settle it in one run. **NEEDS A LIVE RUN.**
- **Whether the bot owns bradley/bmp2/m113 in useful numbers at the time infantry is in the bubble.** The composition-ceiling / cheapest-pump interaction (`DISCOVERIES.md:2426-2433`) makes this non-obvious; I did not trace the shipped composition weights against carrier costs.
- **Whether `StancePositioningExecutor` or `AutoSeekSupplies` can interrupt a boarding walk.** Both are unit-level and `INotifyIdle`-driven; `RideTransport` should keep the unit non-idle, but I did not read their `TickIdle` predicates.
- **The exact TTL passed at `HelicopterSquadBotModule.cs:1287`** — computed at `:1284` as `max(DefaultCommitmentTicks, TransportLoadTimeoutTicks)`; I did not confirm the shipped `PoiGoalGuard.DefaultCommitmentTicks` against `ai.yaml`.
- **Whether the `TransportMissionSlots`/`EvacuateIdleTransports` money-pump (`DISCOVERIES.md:2443`) is live** now that lift launches. The `ShouldBuyTransport` half is fixed; the slot arithmetic is not. **NEEDS A LIVE RUN.**
- **§2's full YAML sweep** was delegated. I re-verified the six load-bearing capacity numbers and the `Passenger.cs:120` type gate directly; the static-building and unreachable-carrier rows I did not re-read.
