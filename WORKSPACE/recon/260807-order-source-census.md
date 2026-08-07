# Order-source census — what issues orders to units today

**Researched against `main` @ `9b39ebf1`** (`git status -sb`: `main...origin/main [ahead 13]`, tree clean apart from untracked scratch). Every claim below carries a `file:line`. Static analysis only — no game runs, no autotests.

**What this document is.** An honest inventory of every code path that commands a unit, so that a proposed single-attention scheduler can be sized against reality. **This document deliberately proposes no design.** Where a fact could not be established it says so; a wrong certainty here is worse than an admitted gap (see §7).

**Timestep.** `mods/ww3mod/mod.yaml:369-372` — default speed `normal` = `Timestep: 60` ms ⇒ **16.667 ticks/s**. All wall-clock conversions below use `seconds = ticks × 0.06`.

**Bot roster.** Exactly two profiles ship: `ModularBot@experimental` and `ModularBot@stable` (`ai.yaml:31-36`). Both hold `enable-ai-player` and `enable-ai-any` (`ai.yaml:41-54`); `@stable` is the frozen benchmark control and has held **full `@experimental` parity since the 2026-08-02 promotion** (`ai.yaml:27-30`).

---

## 0. Headline findings

1. **There are two order-issuance layers, not one.** Everything in `BotModules/` funnels through `ModularBot.QueueOrder` (`ModularBot.cs:91-98`) — but a parallel *unit-level* layer moves units via `self.QueueActivity(...)` and never produces an `Order` at all (§1.4). A scheduler placed at the order layer would be structurally blind to it.
2. **There is no arbitration.** There are **three** disjoint claim registries honoured by **disjoint** subsets of modules, plus a busy-check (`IsIdle`) that the modules' own re-fire timers defeat. The outcome is "last writer wins, silently" — but the mechanism is not a same-tick race (§3).
3. **Every module cadence is a `--countdown` decremented per *call*, not a world-tick stamp.** Withhold a module and its notion of "interval" stretches by the withhold factor. Only three pieces of state in the whole census are genuinely tick-stamped and therefore skip-safe (§6.3).

---

## 1. The census

### 1.1 The funnel — and the two things that bypass it

Every `bot.QueueOrder(...)` in `BotModules/` lands on the single interface-explicit `IBot.QueueOrder` (`ModularBot.cs:91-98`), which **only enqueues** into a private `Queue<Order>` (`:49`). `ITick.Tick` (`:100-139`) ticks the modules first (`:111-116`), then drains a *fraction* of the queue:

```csharp
var ordersToIssueThisTick = Math.Min((orders.Count + info.MinOrderQuotientPerTick - 1) / info.MinOrderQuotientPerTick, orders.Count);  // :127
```

`MinOrderQuotientPerTick = 5` (`:34`, not overridden in `ai.yaml`) ⇒ ~1/5 of pending orders per tick, FIFO (`:131`) into `world.IssueOrder` (`:137`).

Two things bypass this funnel entirely and would be invisible to an order-layer scheduler:

- **`HelicopterSquadBotModule.cs:1722`** — the evacuation path calls `h.QueueActivity(false, new RotateToEdge(...))` directly.
- **`LaneAmbushBotModule.cs:456-462` / `:479-483`** — grants and revokes the `enable-ambush-tactics` `ExternalCondition` token directly on the unit.

Plus the whole unit-level layer in §1.4.

### 1.2 Attached, order-issuing bot modules

Cadence fields are the *actual* `Info` field name; "shipped" is the value in `mods/ww3mod/rules/ai/ai.yaml`. No scoped module appears in `ai-america.yaml` / `ai-russia.yaml` (those carry only `UnitBuilderBotModule` blocks, `ai-america.yaml:7,54` / `ai-russia.yaml:6,53`).

| Module (instance) | Profile | Cadence field → C# default → **shipped** | Can select | Order sites |
|---|---|---|---|---|
| **PoiOffensiveBotModule** `@experimental` `ai.yaml:235` / `@stable` `:1482` | both | `ReevaluateInterval` → 100 (`:58`) → **100** (`:237` / `:1484`) = **6.0 s** | `BuildFreePool` (`:1855-1865`): all `world.Actors`, owned+alive+`IPositionable`+`AttackBase`, **not** `Aircraft`; role `MainBattle`\|`IndirectFire` and `!IsTroopCarrier` under `UseUnitRoles`; minus axis-claimed and ledger-committed. **Excludes `truk`.** Selects units at the SR (forward staging exists for that). | `:2215` `AttackMove` (grouped); `:2722` `SetCohesion`; `:2733`/`:2735` `AttackMove` (grouped; `:2735` **queued** iff a detour precedes it); `:2844` `AttackMove` (per-unit, fires standoff); `:2893`/`:2905`/`:2912` `SetUnitStance`; `:3155` `AttackMove` (bombard anchor); `:3659` `AttackMove` (prep-fires hold); `:3683` `AttackMove` (rally) |
| **PoiGarrisonBotModule** `@experimental` `:622` / `@stable` `:1524` | both | `ReevaluateInterval` → 100 (`:56`) → **100** (`:624`/`:1526`) = **6.0 s** | Same free-pool shape as offense (`:403-435`) — **draws from the same pool**; deconflicted only by the ledger, not by disjoint filters. Excludes trucks. | `:479` `AttackMove`, **grouped**, non-queued |
| **LaneAmbushBotModule** `@experimental` `:665` / `@stable` `:1550` | both | `ReevaluateInterval` → 100 (`:69`) → **100** (`:667`/`:1552`) = **6.0 s** | `:519-553`: owned+`AttackBase`+non-`Aircraft`+`CanHostAmbush` (`:556-568`), role-filtered, not ledger-committed. **No `IsIdle` filter** — takes a busy unit if the ledger is silent. Bounded to `MaxAmbushes(2) × UnitsPerAmbush(2) = 4`. | `:439` `AttackMove` (grouped); `:471`/`:494` `SetUnitStance` |
| **CaptureCoordinatorBotModule** `@experimental.tecn` `:94` / `@stable.tecn` `:1423` | both | **two** countdowns in one tick (`:478-488`): `ScanInterval` → 75 → **75** (`:105`/`:1429`) = **4.5 s**; `DefenseScanInterval` → 150 → **150** (`:131`/`:1445`) = **9.0 s**. Both randomised at enable (`:429-430`). | Capturers: `CapturingActorTypes: tecn,tecn.russia,tecn.america`, rebuilt once from `UnitRole.CaptureSpecialist` under `UseUnitRoles` (`:462-476`); `IsIdle`+not committed (`:538-546`). **Escorts/defenders (`FindIdleSupportersNear`, `:1440-1467`): ANY armed idle owned unit within `SupportRecruitRadiusCells: 40` (`ai.yaml:130`)** — `SupportingUnitTypes` unset, so no whitelist. Includes infantry near the SR. | `:1050` `CaptureActor` **QUEUED**; `:1171` `Move` (retreat to SR); `:1309` `AttackMove` **grouped** (escorts); `:1416` `AttackMove` **grouped** (defenders) |
| **LayeredDefenceBotModule** `@experimental` `:869` / `@stable` `:1601` | both | `ScanInterval` → 75 (`:44`) → **75** (`:871`/`:1603`) = **4.5 s**; staggered at enable (`:208`). Hard-gated on `influenceMap != null` (`:235`) | `:350+`: owned+alive+**`IsIdle`**+role `MainBattle` (`IsLineEligibleByRole`)+off the per-unit `AssignCooldownTicks: 250` cooldown+not out of ammo (`SkipOutOfAmmoUnits`)+**not reserved by MountedTransport** (`:393`)+not ledger-committed (experimental). Cap `MaxAssignsPerScan` (default 4, `:77`). Its intended pool **is** the SR reserve. | `:504` `AttackMove` (per-unit); `:639` `AttackMove` (per-unit, man-the-line) |
| **EngineerRouteOpenBotModule** `@experimental` `:920` | **exp only** (no `@stable` twin; C# default `RouteOpenEnabled=false`) | `ScanInterval` → 100 (`:102`) → **100** (`:923`) = **6.0 s**; fixed offset at enable (`:191`), not randomised | Engineer: owned+`RepairsBridges` (`:441`). Screen: owned+alive+**`IsIdle`**+not in `ExcludedActorTypes` (`ai.yaml:930`: tecn/e6/truk/humvee/btr/bradley/bmp2/m113)+ledger-free, up to `ScreenSize: 3` (`:469-471`). Can take infantry anywhere incl. the SR. | `:288` `RepairBridge` (re-issue); `:361` `RepairBridge`; `:373` `AttackMove` (per screen unit, in a loop) |
| **MountedTransportBotModule** `@poi` `:949` (`enable-ai-stable`) / `@experimental` `:983` | one per player | `ScanInterval` → 100 (`:41`) → **50** (`:955`/`:985`) = **3.0 s**; randomised at enable (`:302`) | Carriers: `CarrierTypes: bradley, bmp2, m113` (`:956`/`:986`), must have `Cargo`, be empty, not in a task; **`IsIdle` deliberately NOT required** (`:492-521`, PITFALL comment). Passengers: `PassengerTypes` (20 infantry names) **within `ReserveZoneRadiusCells: 14` of the SR**, or (exp) within `PickupCorridorCells: 6` of the SR→drop lane (`:563-576`). **This is the module that explicitly targets infantry near the Supply Route.** | `:267` `Stop`; `:268` `EnterTransport` (capture ferry — **event-driven**, called by CaptureCoordinator, not on the timer); `:407` `Unload`; `:419` `Move` **QUEUED**; `:440` `CaptureActor`; `:451` `Move`; `:462` `Unload`; `:483` `Move`; `:617` `Stop`; `:621` `EnterTransport` |
| **HelicopterSquadBotModule** `@stable` `:1245` / `@experimental` `:1265` | one per player | **five** countdowns in one tick (`:503-551`), all per-call, **none staggered**: `SquadUpdateInterval` → **5** (`:146`) = 0.3 s; `ScanInterval` → **100** (`:143`) = 6.0 s; `AttackCooldown` → **900** (`:1249`/`:1269`) = 54 s; `ScoutInterval` → **400** (`:1250`/`:1277`) = 24 s; `TransportInterval` → **600** (`:1251`/`:1278`) = 36 s. Plus `EvaluateIdleHelicopters()` **unconditionally every tick** (`:550`) | Helis: any owned actor with `AIHelicopterRole` (`:559-561`); claimed in `BotBlackboard` as `"helicopter"` (`:569`). **Lift passengers (`IsLiftCandidate`, `:1582-1609`): infantry with `WithInfantryBody`, role `MainBattle` (`RestrictLiftToLineInfantry`, C# default `true`), not reserved by MountedTransport, not ledger-committed, and within `LiftReserveZoneRadiusCells` (default 14) of the own SR (`:1609`).** Direct overlap with MountedTransport and LayeredDefence on the SR reserve pool. | `:716` `Move` (staging); `:890` `Move` (scout); `:1075` `EnterTransport`; `:1242` `Stop`; `:1252` `Move` + `:1253` `Unload` **QUEUED** + `:1257` `Move` **QUEUED** (a three-order chain on one actor); `:1359` `Unload`. **Attack squads issue no orders here** — those come from the squad FSM at the 5-tick cadence |
| **GarrisonBotModule** `@defenses` `:710` | **shared `enable-ai-any` instance — runs for both bots** | `ScanInterval` → 150 (`:28`) → **200** (`:712`) = **12.0 s**; no stagger (`:120-123`) | **`GarrisonActorTypes` is NOT set in `ai.yaml:710-720`**, so the fallback at `:217` admits **any actor with `PassengerInfo`**. Owned+`Mobile`+`IsIdle`+not blackboard-claimed+(exp only) not ledger-committed (`:153-163`). Distance filter is on the *buildings* (`MaxGarrisonRadius: 25` from a **random** owned Building at init, `:112`), **not on the infantry** — it can pull infantry from anywhere on the map. **`^TECN` carries `Passenger:` (`infantry.yaml:2209`) and there is no capturer exclusion ⇒ it can garrison a technician.** Trucks excluded (`TRUK`, `vehicles.yaml:510`, has no `Passenger`). | `:188` `EnterTransport`, non-queued, capped by `MaxOrdersPerTick` (default 3, **shipped 2**, `:714`) |
| **ScoutBotModule** `@america` `:693` / `@russia` `:701` | both (`enable-ai-any && player.nato/brics`) | `ScanInterval` → 200 (`:30`) → **200** (`:697`/`:705`) = **12.0 s**; **no stagger** — countdown starts at 0, fires on the first bot tick (`:98-101`) | `MaxScouts: 2`; owned `Mobile` with `Info.Name == scoutType` (`humvee`/`btr`), **`IsIdle`**, not blackboard-claimed (`:141-147`). `humvee`/`btr` are in the POI stack's `ExcludedActorTypes` ⇒ no overlap by design | `:128` `Move` (per-scout, non-queued) |
| **SupplyFollowerBotModule** `@supply` `:723` | **shared `enable-ai-any` instance — runs for both bots**; exp-only behaviours double-gated in C# (`:191`, `:194`) | `ScanInterval` → 120 (`:28`) → **150** (`:726`) = **9.0 s** (`:210-213`) | **Supply trucks only** — `SupplyTruckTypes: truk` (`:725`), owned+alive+not blackboard-claimed+not low on supply (`:232-239`). Reads (never orders) other owned `Mobile` actors to form clusters (`:245-247`) | all `Move`, per-truck: `:331` (danger evac), `:353`, `:377`+`:378` (**`:378` QUEUED**, detour pair), `:386`, `:492` (idle-truck hunt) |
| **BaseBuilderBotModule** `@normal` `:1017` | both (`enable-ai-player`) — the `@normal` suffix is a misnomer, not legacy | **No `BotTick` countdown** (`:183`) — runs every tick; cadence lives in `BaseBuilderQueueManager.waitTicks` (`:91`), reset to `StructureProductionActiveDelay` 25 / `Inactive` 125 **+ `world.LocalRandom.Next(0, 10)`** (`:103-106`) | Own `RallyPoint`-carrying actors (`:210-214`) and own production queues. **No combat units, trucks or infantry** | `:217` `SetRallyPoint`; `BaseBuilderQueueManager.cs:120` `StartProduction`, `:160` `CancelProduction`, `:174` `PlaceBuilding`/`PlacePlug` |
| **BuildingRepairBotModule** `@aiplayer` `:686` | both | **No cadence, no `IBotTick`** — `IBotRespondToAttack` only (`:23`), an interrupt on the damage-state transition | Nothing; `self` is the damaged building | `:44` `RepairBuilding`, non-queued |

### 1.3 Attached but issuing no unit orders

- **`AdaptiveProductionBotModule`** ×4 (`:768`, `:812`, `:1563`, `:1582`), `EvaluationInterval` → 500 → **300** = 18.0 s (`:217-220`). **Zero `QueueOrder`/`IssueOrder` sites in the file.** Acts only through `IBotRequestUnitProduction` (`:327`, `:437`, `:563`). A budget actor, not an order actor — it never competes for a unit.
- **`UnitBuilderBotModule`** ×6+ (`ai.yaml:1055/1099/1126/1151/1192/1215`, `ai-america.yaml:7/54`, `ai-russia.yaml:6/53`). Cadence is **not** a YAML field: `ticks++; if (ticks % FeedbackTime == 0)` with `FeedbackTime = 30` a **`const`** (`:223,376-378`) = 1.8 s. Orders target the production queue actor (`:481`, `:671` `Order.StartProduction`), never a unit.

### 1.4 The unit-level layer — orders that are not Orders

These are per-unit traits that move units by queueing an activity directly. **None of them produces an `Order`, so none appears in any `QueueOrder`/`IssueOrder` audit** — but all of them visibly move units.

| Trait | Attached | Gate | Cadence | What it does |
|---|---|---|---|---|
| **`StancePositioningExecutor`** | `defaults.yaml:27`, under `^Combatant`; `Requires<MobileInfo>` (`:65`) | `RequiresCondition: enable-tactical-positioning \|\| enable-ai-experimental` (`defaults.yaml:28`). Per-unit grants: `Bots: experimental` (`defaults.yaml:36-38`) **and `GrantConditionOnHumanOwner@tacpos` — default-ON for every human-owned combatant** (`defaults.yaml:41-45`) | `EvaluateCooldown: 30` (`defaults.yaml:30`) = **1.8 s**; `INotifyIdle, ITick` (`:130`) | `self.QueueActivity(new Move(self, dest))` (`:414`). **Also writes `tacpos:<actorID>` to the shared ledger (`:643`, TTL `ClaimTicks: 150`) — it is a write-only ledger participant, it never reads** |
| **`CohesionSlotMemory`** | `defaults.yaml:20`, under `^Combatant` — declared **before** the executor deliberately (`defaults.yaml:21-23`) | — | `INotifyIdle.TickIdle` | `self.QueueActivity(new Move(self, assignedSlot))` (`:227`), `new Turn(...)` (`:199`) — returns a unit to its formation slot |
| **`AutoSeekSupplies`** | **`^Soldier`** (`infantry.yaml:221`), `Enabled: true` | none — the YAML comment at `infantry.yaml:217-220` states it outright: *"This is a TRAIT, not a bot module, so the one switch covers every soldier — human- and bot-owned alike; there is no owner-side split."* Vetoed only by stance (`SupplyHuntMath.StancesPermitHunt`) | `ScanInterval: 40` (`:48`) = **2.4 s** | `self.QueueActivity(false, new SeekSuppliesAndReturn(...))` (`:112`) when an idle soldier drops below `AutoSeekAmmoThresholdPerMille: 250` within `SupplyHuntLeashCells: 20` |
| **`GarrisonManager`** | `civilian.yaml:63`, `structures-defenses.yaml:125/218/306` | — | `ITick` per-tick FSM (`:573+`) | Deploys/recalls/re-targets garrisoned infantry by **direct trait manipulation** (`SetCenterPosition`, `RecallToShelter`, `PromoteFromShelter`) — zero orders. A parallel controller, not an order conflict |
| `ScaredyCat` / `Wanders` | `^CivInfantry` only (`infantry.yaml:334`, `:337`) | — | — | Autonomous, but **on no bot-owned unit today**. `ScaredyCat:118` does call `world.IssueOrder(..., queued: true)` |
| `AutoFollowAlly` | `^MEDI` (`infantry.yaml:2148`) | — | — | `self.QueueActivity(false, move.MoveWithinRange(...))` (`:88`) |

`^Combatant` coverage: inherited by `^CamoSoldier` (`infantry.yaml:257`) — which is the base for `^E3` (`:1165`) and `^AT` (`:1655`), i.e. the line infantry — and by 16 named combat-vehicle templates (`vehicles-america.yaml`, `vehicles-russia.yaml`, 8 each). **Not** by `TRUK` (`vehicles.yaml:510`, no `^Combatant` inherit) and not by aircraft.

Reactive-only plumbing (issues orders/activities *in response to* an order already given — harmless to a scheduler): `AttacksSupplyRoutes.cs:45,80`; `RallyPoint.cs:103,155`; `Minelayer.cs:155,169,174`.

### 1.5 Non-participants (present in C#, not attached in ww3mod)

Verified by grep over all of `mods/`. Each is a finding, not a gap:

`McvManagerBotModule`, `SupportPowerBotModule` (`:114`), `HarvesterBotModule` (`:199`), `CaptureManagerBotModule` (`:163` — deliberately superseded, `ai.yaml:101-102`), `BotModuleLogic/MinelayerBotModule` (`:230`, `:238`), `Carryall`, `DockClientManager`, `Harvester`.

**The entire ground/naval/protection squad layer is dead code.** All four `SquadManagerBotModule` instances (`ai.yaml:1075`, `:1166`, `:1617`, `:1635`) set `IgnoreGroundUnits: true` (`:1086`, `:1177`, `:1630`, `:1643`). Ground units therefore hit `continue` at `SquadManagerBotModule.cs:329-336` and are never added to `unitsHangingAroundTheBase`, so `CreateAttackForce` always early-returns at `:370-371`. With `NavalUnitsTypes` and `ProtectionTypes` set on no instance, **only `SquadType.Air` is ever created**. Consequently `Squads/States/GroundStates.cs` (9 order sites), `NavyStates.cs` (5), and `ProtectionStates.cs` (1) are all unreachable — as is `StateBase.ExcludeTacticallyCommitted` (`:155-171`).

> **Doc-staleness flag (not fixed here — this task is read-only).** `DOCS/reference/architecture.md:321` says a `GroundStates`-based change *"touches only the legacy/`@stable`/normal profiles that still let SquadManager own ground"*. That is **false at `9b39ebf1`**: `@stable` sets `IgnoreGroundUnits: true` at `ai.yaml:1630` and `:1643`, and the legacy/normal profiles were removed 2026-07-30. `GroundStates` is unreachable on **both** shipped profiles. Recommend correcting.

### 1.6 The live squad layer

`Squad.Update()` → `FuzzyStateMachine.Update` → `currentState.Tick(squad)` (`Squad.cs:88-92`, `StateMachine.cs:18-21`). Two drivers:

- `SquadManagerBotModule.cs:277-278` — every `AttackForceInterval` (C# default **75**, not overridden) = 4.5 s. Air squads only.
- `HelicopterSquadBotModule.cs:765` — every `SquadUpdateInterval` = **5** ticks (`:146`) = 0.3 s.

Live order sites: **`AirStates.cs`** `:187` `ReturnToBase`, `:193` `Attack`, `:217` `ReturnToBase`, `:221` `Move` — all per-unit, non-queued. **`HelicopterStates.cs`** `:103`/`:115` `ReturnToBase`, `:639` `AttackMove`, `:642` `Attack`, `:745` `ReturnToBase`, `:753` `Attack`, `:847` `Move`, `:908` `ReturnToBase` — all per-unit, non-queued.

---

## 2. Overlap — the conflict matrix

**Overall shape: one large contested pool, one narrow exclusive pool, and one pool contested across a registry boundary.**

### 2.1 The contested pool: idle armed ground units (the SR reserve)

Seven consumers draw from overlapping sets of the same units. This is the whole conflict surface.

| Consumer | Its filter on the shared pool | Distance bound |
|---|---|---|
| `PoiOffensiveBotModule` | role `MainBattle`\|`IndirectFire`, `!IsTroopCarrier`, non-`Aircraft` | **none** — scans all `world.Actors` (`:1860`) |
| `PoiGarrisonBotModule` | identical shape (`:415-435`) | none |
| `LayeredDefenceBotModule` | role `MainBattle` only, `IsIdle`, has ammo | influence-map driven |
| `LaneAmbushBotModule` | `CanHostAmbush` + role; **no `IsIdle`** | none; capped at 4 units |
| `CaptureCoordinatorBotModule` (escorts/defenders) | **any armed `IsIdle` unit** — no type whitelist | 40 cells of the capturer |
| `EngineerRouteOpenBotModule` (screen) | `IsIdle`, not in `ExcludedActorTypes` | none; capped at 3 |
| `HelicopterSquadBotModule` (lift passengers) | infantry, role `MainBattle` | **14 cells of the own SR** |
| `MountedTransportBotModule` (passengers) | `PassengerTypes` infantry | **14 cells of the own SR** (+6-cell corridor on exp) |

**Infantry standing near the Supply Route are the single most contested class**: they satisfy LayeredDefence's line pool, both transports' reserve bubbles, CaptureCoordinator's escort radius, EngineerRouteOpen's screen, LaneAmbush, and — since `GarrisonActorTypes` is unset — GarrisonBotModule's unbounded `PassengerInfo` sweep.

### 2.2 The exclusive pool: supply trucks

`truk` appears in `ExcludeUnitTypes` / `ExcludedActorTypes` / `ExcludeFromSquadsTypes` on **every** other module (`ai.yaml:366, 635, 679, 906, 930, 1087, 1178, 1498, 1534, 1560, 1615, 1631, 1644`), and is named only by `SupplyFollowerBotModule` (`SupplyTruckTypes: truk`, `:725`). At the module layer, **supply trucks are cleanly single-owner.** They are also outside the unit-level layer (`TRUK` does not inherit `^Combatant`). The residual truck risk is not order conflict but `AmmoPool` auto-evac (`InitialResupplyBehaviorAI: Evacuate`, `vehicles.yaml:516`), which is invisible to every bot module (`architecture.md:331`).

### 2.3 The cross-registry conflict: TECN

A technician is selectable by **`CaptureCoordinatorBotModule`** (capturer pool, ledger-participating) *and* by **`GarrisonBotModule`** (via the unset-`GarrisonActorTypes` fallback at `:217`, since `^TECN` has `Passenger:` at `infantry.yaml:2209`). These two arbitrate through **different registries**: CaptureCoordinator uses `PoiGoalGuard`, Garrison uses `BotBlackboard.ClaimUnit` — and on **`@stable`** Garrison's ledger participation is switched off by the runtime `isExperimentalBot` gate (`GarrisonBotModule.cs:103`), so on `@stable` nothing at all deconflicts them. `GarrisonBotModule.cs:38` says this in its own `[Desc]`: *"module is ledger-blind — its only lock is `BotBlackboard.ClaimUnit`, invisible to the POI stack."*

Because captures **consume** the technician (`ConsumedByCapture: true`, `infantry.yaml:903`; `game-model.md:35`) and technician availability — not coordinator logic — is the binding constraint on the whole capture game, this is the most consequential single overlap in the census.

### 2.4 Units in a squad

Squad members are **air units only** (§1.5), and the ground modules all exclude `Aircraft`, so there is no live squad/ground contention. It is masked, not solved: `StateBase.ExcludeTacticallyCommitted` (`:155-171`) filters on `objective.StartsWith("tacpos:")` **only** (`:170`), so every non-`tacpos:` commitment is invisible to squads — and squads never write either registry. If `IgnoreGroundUnits` ever flips, this becomes live immediately.

---

## 3. Conflict resolution — what actually happens today

**Verdict on the working hypothesis ("last writer wins, silently"): the OUTCOME is confirmed; the MECHANISM is not a same-tick overwrite.**

### 3.1 Confirmed

- **Non-queued orders hard-cancel the running activity.** `Actor.cs:381-387`: `QueueActivity(bool queued, Activity next) { if (!queued) CancelActivity(); QueueActivity(next); }`. `CancelActivity()` → `CurrentActivity?.Cancel(this)` (`:400-403`); `Activity.Cancel` (`Activities/Activity.cs:198-210`) **nulls `NextActivity`, dropping the entire queued chain**. Consumers forward `order.Queued` verbatim: `Mobile.cs:1016`, `AttackMove.cs:110`, `AttackBase.cs:466`→`:637`.
- **Bot orders are overwhelmingly non-queued.** Across `BotModules/`, `queued` is `false` roughly 23:1. The exceptions are the deliberate chains listed in §1.2 (`CaptureCoordinator:1050`, `HelicopterSquad:1253/1257`, `MountedTransport:419`, `SupplyFollower:378`, `PoiOffensive:2735`, `McvManager:175`). **Every squad-state order is non-queued.**
- **The loser learns nothing.** `QueueOrder` returns `void` (`ModularBot.cs:91`). No callback, no return value, no exception. A silently-dropped order is indistinguishable from a delivered one — note `ModularBot.cs:134-135` `continue`s (discards) orders for player-controlled actors with no notification at all.
- **No logging in normal play.** `UnitLifecycleLogger.LogOrder` (`:343-383`) records the issuing module via `ModularBot.currentModuleTag` (`ModularBot.cs:96,114,155`), but only when `TestMode.IsActive && TestMode.UnitLifecycleLogPath` is set (`:144-160`) — and it records *issuance*, never *overwrite*. Offline order-churn detection is specified but **not implemented** (`tools/behavior-lint/README.md:46`, R5 "order churn" listed as part of the full build).
- **Retry is blind.** Modules re-issue on their own countdown, not in response to loss. That is the thrash loop.
- **Partial progress is destroyed, not paused.** Movement survives only to the cell boundary (`Mobile.cs:705` sets `IsInterruptible = false` mid-traversal — one of only five such sites, with `DeployForGrantedCondition.cs:66`, `Parachute.cs:28`, `Sell.cs:32`, `Transform.cs:65`). Capture is **not** in that list, so a partial capture approach is lost.

### 3.2 Refuted — it is not a same-tick race

Bot orders do not resolve in the tick they are queued. `ModularBot.Tick` enqueues, then drains ≤ ⌈N/5⌉ per tick (`:127`) into `world.IssueOrder` → `OrderManager.IssueOrder` (`World.cs:157`) → `localOrders` (`OrderManager.cs:131-137`) → `SendOrders` (`:223-231`) → `ProcessOrders` (`:233-284`) → `UnitOrders.ProcessOrder` (`:256`) → `ResolveOrder` (`UnitOrders.cs:420-427`) → `Actor.ResolveOrder` (`Actor.cs:476-480`). `Game.InnerLogicTick` (`Game.cs:771-820`) runs `TickImmediate` (`:795`) → `ProcessOrders` (`:804`) → `world.Tick()` (`:808`), and `EchoConnection.Receive` projects orders forward one frame (`Connection.cs:87`). **Net: ≥2 world ticks of latency, plus more from the 1/5 throttle.** The winner is decided by *arrival order at `ResolveOrder`*, i.e. FIFO of the module tick order.

### 3.3 What fixes the module tick order — and why it is fragile

`ModularBot.Activate` snapshots the modules **once**: `tickModules = p.PlayerActor.TraitsImplementing<IBotTick>().ToArray()` (`:84`), iterated in array order every tick (`:111`). The chain:

`Actor.TraitsImplementing<T>` → `World.TraitDict.WithInterface<T>` (`Actor.cs:439-441`) → `TraitContainer<T>.GetMultiple`, walking a `List<T>` in **insertion order** (`TraitDictionary.cs:146-147,180`) → trait construction order (`Actor.cs:182-185`) → `ActorInfo.TraitsInConstructOrder` (`GameRules/ActorInfo.cs:104-142`), a topological sort seeded from `TypeDictionary` **insertion order** (`Primitives/TypeDictionary.cs:80-85`). Bot modules declare no `Requires<>` on each other, so they all resolve in the first pass (`:117`) in source order.

**Therefore the same-conflict winner is whichever module is declared LATER in `mods/ww3mod/rules/ai/ai.yaml`.** Nothing in code declares this; it is an emergent property of YAML line order.

### 3.4 Is there any arbitration? — three registries, none binding

| Mechanism | Binds | Does **not** bind |
|---|---|---|
| **`PoiGoalGuard.Ledger`** (`PoiGoalGuard.cs:39-117`) — TTL commitment, `IsCommitted` (`:81-82`) | the POI/maneuver stack (§4) | squads except via the `tacpos:`-only filter (`StateBase.cs:170`); Scout; SupplyFollower; Garrison and LayeredDefence on `@stable` |
| **`BotBlackboard.ClaimUnit`** (`BotBlackboard.cs:196-211`) — single-writer mutex on `Dictionary<uint,string>` (`:84`) | **writers:** `GarrisonBotModule:192`, `HelicopterSquadBotModule:569`, `ScoutBotModule:155`, `SupplyFollowerBotModule:338/395/498`. **readers:** `GarrisonBotModule:159`, `ScoutBotModule:146`, `SupplyFollowerBotModule:237` | the entire POI stack (zero references). **`HelicopterSquadBotModule` is write-only** — it claims but never reads, so it will take a unit another blackboard module already claimed |
| **`BotBlackboard` task API** (`PostTask`/`ClaimTask`/`GetOpenTasks`, `:137-191`) | — | **zero callers. Dead code.** |
| **`IsIdle`** (`Actor.cs:75`, `CurrentActivity == null`) — 57 uses across bot modules | nothing | it is not a lock; modules re-fire on timers regardless, so a unit busy under module A is non-idle for one scan and re-grabbed on the next. This is precisely the flicker the ledger was written to fix (`PoiGoalGuard.cs:6-17`) |
| **`IValidateOrder`** (`UnitOrders.cs:425`, `World.cs:148/255`) — the only true veto seam | sole implementation `Traits/World/ValidateOrder.cs:21-52`: ownership/exploit checks (`:48`) + `AcceptsOrder` (`:51`) | **no module-conflict awareness whatsoever.** It is, however, the natural chokepoint a future scheduler could occupy |
| `ControlAllUnitsManager.IsPlayerControlled` (`ModularBot.cs:134`) | human takeover of a bot's units | inter-module conflict |
| `HealerClaimLayer` (`Traits/World/HealerClaimLayer.cs`) / `ResourceClaimLayer` | claims **targets**, not units | a different axis entirely |

**Plainly: there is no arbitration.** There are two half-built unit registries honoured by disjoint module subsets, a dead task API, and a busy-check the modules' own timers defeat.

---

## 4. `PoiGoalGuard` and its ledger

**Shape.** `PoiGoalGuard@poi` (`ai.yaml:83-89`) is `RequiresCondition: enable-ai-experimental || enable-ai-stable`, `DefaultCommitmentTicks: 600`. It is **per-player** — `[TraitLocation(SystemActors.Player)]` (`PoiGoalGuard.cs:304`), fetched by every consumer via `player.PlayerActor.TraitOrDefault<PoiGoalGuard>()` (e.g. `PoiOffensiveBotModule.cs:1039`, `StateBase.cs:157`, `StancePositioningExecutor.cs:642`). It is a **single un-twinned instance per player** (rationale `ai.yaml:78-82`): both bot profiles share one setting. *(Note: the doc comment at `PoiGoalGuard.cs:296` — "a single instance runs for BOTH bots" — is imprecise. What is shared is the YAML block `GarrisonBotModule@defenses` on `enable-ai-any`; each player still gets its own module and its own ledger.)*

Objective keys are **disjoint prefixes** so claims stay attributable: `offense:` / `bombard:` / `capture:` / `capture-escort:` / `capture-defend:` / `transport:` / `garrison:` / `defend:` / `defend-line:<x>,<y>` / `ambush:` / `tacpos:`.

### 4.1 Participation table

| Participant | Writes | Reads | Gate flag (C# default) | Shipped | Active in |
|---|---|---|---|---|---|
| `PoiOffensiveBotModule` | `:1784`, `:2473`, `:3143` | `:1863` | — | — | exp + stable |
| `PoiGarrisonBotModule` | `:468` | `:411`, `:450` | — | — | exp + stable |
| `LaneAmbushBotModule` | `:421`/`:424` | `:525` | — | — | exp + stable |
| `CaptureCoordinator` (capturer) | `:1053` | `:542`, `:1029`, `:1456` | — | — | exp + stable |
| `CaptureCoordinator` (escort/defender) | `:1321`, `:1428` | — | `CommitSupportUnits` (**false**, `:277`) | `true` `ai.yaml:204`; **absent from `@stable.tecn`** | **exp only** |
| `LayeredDefenceBotModule` | `:511`, `:645` | `:400` | `CommitLineAssignments` / `RespectCommitmentLedger` (**both false**, `:131`) | both `true` `ai.yaml:887`; **both absent from `@stable`** | **exp only** — `@stable` is a total non-participant |
| `MountedTransportBotModule` | `:206` | `:571` | `CommitPassengers` (**false**, `:125`) — **also gates resolution at `:313`** | `true` `ai.yaml:1014`; **absent from `@poi`** | **exp only** |
| `HelicopterSquadBotModule` | `:1287` | `:1606` (**unconditional**) | `CommitTransportPassengers` (**false**, `:108`) | `true` `ai.yaml:1339`; **absent from `@stable`** | **reads on both, writes exp only** |
| `GarrisonBotModule@defenses` | `:198` | `:162` | `ShouldCommitShared(CommitGarrisonedUnits, ledger, isExperimentalBot)` (**false**, `:43`) | `true` on the shared `enable-ai-any` block `ai.yaml:720` | **exp only** (runtime `BotType` gate, `:103`) |
| `EngineerRouteOpenBotModule` | `:501` | `:445`, `:477` | `RouteOpenEnabled` (**false**, `:99`) | `true` `ai.yaml:922`; **no `@stable` twin exists** | **exp only** |
| `StancePositioningExecutor` (per-**unit** trait) | `:643` (`tacpos:`, TTL `ClaimTicks: 150`) | **never** | `self.Owner.IsBot` (`:640`) + per-unit condition | `Bots: experimental` | **exp only — WRITE-ONLY** |
| `UnitBuilderBotModule` | — | `:599` | — | — | exp + stable |
| `SquadManager` / `StateBase` | — | `tacpos:` **only** (`:170`) | — | — | exp + stable (but dead, §1.5) |

### 4.2 The asymmetry — read-only and write-only participants

Per the rule at `WORKSPACE/DISCOVERIES.md:2350`: *a shared ledger only arbitrates between modules that BOTH write it; a flag gating ledger resolution silently gates participation.* Consequences at `9b39ebf1`:

- **`HelicopterSquadBotModule` on `@stable` is READ-ONLY.** It reads unconditionally (`:1606`) but `CommitTransportPassengers` is absent from `ai.yaml:1245-1260`, so its lift passengers are never claimed. Every unconditional writer (offense `:2473`, garrison `:468`, ambush `:424`, capture `:1053`) will poach a soldier mid-board. This is the exact failure mode of the transport discovery, now sitting on the *other* transport module.
- **`LayeredDefenceBotModule` on `@stable` participates in neither direction** — both flags absent, and `:215-216` gates resolution on them. Recorded as an accepted residual at `DISCOVERIES.md:2340`.
- **`StancePositioningExecutor` is WRITE-ONLY.** It never reads, so it stamps `tacpos:` over another module's claim.
- **`SquadManagerBotModule`** reads only `tacpos:` keys and never writes.

**What a non-participant loses:** its just-ordered units stay absent from every other module's `IsCommitted` filter, so `PoiOffensive:1863`, `PoiGarrison:411`, `LaneAmbush:526`, `CaptureCoordinator:1456` and `MountedTransport:571` all treat them as free and re-order them. **What a non-reader loses:** it recruits units already committed elsewhere, and its own `Commit` overwrites their objective (`PoiGoalGuard.cs:62-66` — a different objective starts a fresh entry, `CommitCount` reset to 1), destroying the other claim outright.

### 4.3 The ledger has no priority model

`GoalGuardLedger.Commit` (`:60-77`) is **last-writer-wins on a different objective**. `PoiOffensiveBotModule.CommitAndOrder` re-commits its whole axis every eval, silently overwriting a `tacpos:` or `defend-line:` claim on any unit it holds. The ledger reduces thrash; it does not resolve priority.

### 4.4 Prune is hygiene, not correctness

`Prune` is called at `PoiOffensiveBotModule.cs:1125`, `PoiGarrisonBotModule.cs:237`, `LaneAmbushBotModule.cs:236`, `CaptureCoordinatorBotModule.cs:1139` — all four active on both profiles. Independently, **a stale commitment cannot lock a unit**: `IsCommitted` (`:81-82`) tests `currentTick < ExpiresAtTick` directly. Note `TryGetObjective` (`:84-94`) does **not** check expiry — `CaptureCoordinator:1133-1134` relies on that deliberately; `StateBase:167-168` pairs it with `IsCommitted`, correctly.

### 4.5 Three-tier timer ordering

`ReevaluateInterval` (100) < `AxisCommitmentTicks` (250, `ai.yaml:250` on `@experimental` and `:1495` on `@stable` — **identical**) < `MissionCommitmentWindowTicks` (400, `@experimental` only, `ai.yaml:265`). Each re-eval re-asserts the claim with a fresh TTL. **If the re-eval interval ever exceeds the TTL, the claim lapses between two evals and the unit is released mid-mission.** This is the constraint a scheduler most directly threatens (§6.2).

---

## 5. Cadence reality

### 5.1 Every shipped interval, one table

| Interval | Ticks | Wall-clock @ 60 ms | Source |
|---|---|---|---|
| `HelicopterSquadBotModule.EvaluateIdleHelicopters` | **every tick** | 0.06 s | `:550` |
| `BaseBuilderBotModule.BotTick` | **every tick** | 0.06 s | `:183` |
| `SquadManagerBotModule.AssignRolesToIdleUnits` | **every tick** | 0.06 s | `:223-226` |
| `HelicopterSquadBotModule.SquadUpdateInterval` | 5 | 0.30 s | `:146` (C# default; unset in yaml) |
| `SquadManagerBotModule.MinimumAttackForceDelay` | 0 | every tick | C# default; unset |
| `BaseBuilderQueueManager` active delay | 25 (+0-10 rand) | 1.5–2.1 s | `:103-106` |
| `UnitBuilderBotModule.FeedbackTime` | 30 (**`const`**) | 1.8 s | `:223,376-378` |
| `StancePositioningExecutor.EvaluateCooldown` | 30 | 1.8 s | `defaults.yaml:30` |
| `AutoSeekSupplies.ScanInterval` | 40 | 2.4 s | `:48` |
| `SquadManagerBotModule.AssignRolesInterval` | 50 | 3.0 s | `:66` (C# default; unset) |
| `MountedTransportBotModule.ScanInterval` | **50** | 3.0 s | `ai.yaml:955`, `:985` |
| `SquadManagerBotModule.AttackForceInterval` (squad FSM tick) | 75 | 4.5 s | `:72` (C# default; unset) |
| `CaptureCoordinatorBotModule.ScanInterval` | **75** | 4.5 s | `ai.yaml:105`, `:1429` |
| `LayeredDefenceBotModule.ScanInterval` | **75** | 4.5 s | `ai.yaml:871`, `:1603` |
| `PoiOffensive` / `PoiGarrison` / `LaneAmbush` `ReevaluateInterval` | **100** | 6.0 s | `ai.yaml:237/624/667` + `:1484/1526/1552` |
| `EngineerRouteOpenBotModule.ScanInterval` | **100** | 6.0 s | `ai.yaml:923` |
| `HelicopterSquadBotModule.ScanInterval` | 100 | 6.0 s | `:143` (C# default; unset) |
| `BaseBuilderQueueManager` inactive delay | 125 (+0-10) | 7.5–8.1 s | `:103-106` |
| `CaptureCoordinatorBotModule.DefenseScanInterval` | **150** | 9.0 s | `ai.yaml:131`, `:1445` |
| `SupplyFollowerBotModule.ScanInterval` | **150** | 9.0 s | `ai.yaml:726` |
| `GarrisonBotModule.ScanInterval` | **200** | 12.0 s | `ai.yaml:712` |
| `ScoutBotModule.ScanInterval` | **200** | 12.0 s | `ai.yaml:697`, `:705` |
| `AdaptiveProductionBotModule.EvaluationInterval` | **300** | 18.0 s | `ai.yaml:770/814/1565/1584` |
| `BotBlackboard.CleanupInterval` | **300** | 18.0 s | `ai.yaml:60` |
| `HelicopterSquadBotModule.ScoutInterval` | **400** | 24.0 s | `ai.yaml:1250`, `:1277` |
| `SquadManagerBotModule.RushInterval` | **600** | 36.0 s | `ai.yaml:1079/1170/1621/1639` |
| `HelicopterSquadBotModule.TransportInterval` | **600** | 36.0 s | `ai.yaml:1251`, `:1278` |
| `HelicopterSquadBotModule.AttackCooldown` | **900** | 54.0 s | `ai.yaml:1249`, `:1269` |
| **Commitment TTLs** | | | |
| `StancePositioningExecutor.ClaimTicks` | 150 | 9.0 s | `:114` |
| `AxisCommitmentTicks` / `GarrisonCommitmentTicks` / `AmbushCommitmentTicks` / `AssignCooldownTicks` | **250** | 15.0 s | `ai.yaml:250/630/673/872` (+ stable twins) |
| `EngineerRouteOpen.CommitTtlTicks` | **400** | 24.0 s | `ai.yaml:929` |
| `MissionCommitmentWindowTicks` | **400** | 24.0 s | `ai.yaml:265` (exp only) |
| `PoiGoalGuard.DefaultCommitmentTicks` | **600** | 36.0 s | `ai.yaml:89` |
| `BotBlackboard.TaskStaleTicks` | 1500 | 90.0 s | `ai.yaml:59` |

### 5.2 How often does a frontline unit get a fresh order?

**Order of magnitude: every few seconds. Roughly 5 s from the module layer when uncommitted, and ~2 s from the unit layer regardless.**

Derivation:

- **A committed unit** (holding a live ledger claim) is invisible to `BuildFreePool` in every ledger-reading module, so it receives orders only from its owner. Owners re-eval every 75–100 ticks (4.5–6.0 s) but mostly **dedupe**: `PoiOffensive` and `LaneAmbush` re-issue only when the unit set changed or the destination moved ≥ `RepathThresholdCells: 3` (`ai.yaml:252`, `LaneAmbush:427-431`); `PoiGarrison` gates on `OrderedCell`/`HasOrdered` (`:473-481`). So a *stationary-objective* committed unit may go many evals without a new order.
- **An uncommitted idle unit** is exposed to the seven §2.1 consumers whose scans land at 75/100/150/200-tick offsets. The fastest recurring exposure is 75 ticks ⇒ **a fresh order at least every ~4.5 s**, and if two consumers both want it, on alternating scans indefinitely — the thrash loop.
- **Independently of all of the above**, an idle `^Combatant` under `@experimental` can be repositioned by `StancePositioningExecutor` every `EvaluateCooldown: 30` = **1.8 s** (`defaults.yaml:30`), and an idle low-ammo soldier can walk off to a supply truck every `AutoSeekSupplies.ScanInterval: 40` = **2.4 s** (`:48`). Neither is an `Order`.

For contrast, the human-attention model under consideration would leave a group untouched for tens of seconds. The gap is roughly **one order of magnitude**.

---

## 6. What would have to change — an impact list

*(Impact only. No design is proposed; that is a separate effort.)*

### 6.1 Would have to become a *request*

Every site in §1.2 that targets a **unit** and competes for the shared pool: `PoiOffensiveBotModule` (9 sites), `PoiGarrisonBotModule` (`:479`), `LaneAmbushBotModule` (`:439`, `:471`, `:494`), `CaptureCoordinatorBotModule` (`:1050`, `:1171`, `:1309`, `:1416`), `LayeredDefenceBotModule` (`:504`, `:639`), `EngineerRouteOpenBotModule` (`:288`, `:361`, `:373`), `MountedTransportBotModule` (10 sites), `HelicopterSquadBotModule` (`:716`, `:890`, `:1075`, `:1242`, `:1252`/`:1253`/`:1257`, `:1359`), `GarrisonBotModule` (`:188`), `SupplyFollowerBotModule` (6 sites), `ScoutBotModule` (`:128`), and the live squad states (`AirStates` ×4, `HelicopterStates` ×8).

### 6.2 Genuinely fine as direct orders

- **`BuildingRepairBotModule:44`** — `RepairBuilding` on a building, event-driven, competes for no unit.
- **`BaseBuilderBotModule:217`** (`SetRallyPoint`) and `BaseBuilderQueueManager:120/160/174` — production/placement against structures.
- **`UnitBuilderBotModule:481/671`** and all `AdaptiveProductionBotModule` requests — production queue, not units. **But note the coupling**: the priority FIFO drains one item per 30-tick cycle (`:389-390`), so a scheduler that starves `UnitBuilderBotModule` stalls all reinforcement.
- **`SupplyFollowerBotModule`'s truck orders** — single-owner pool (§2.2); no conflict to arbitrate, though they still consume attention if attention is global.
- **`ScoutBotModule:128`** — disjoint unit types (`humvee`/`btr` are excluded everywhere else) and the most scheduler-friendly design in the census: it re-orders purely on `IsIdle`, carries no staged sequence.
- The stance/cohesion orders (`SetUnitStance`, `SetCohesion`) are modifiers on units the issuer already holds, not claims.

### 6.3 Modules whose state machine assumes a fixed re-run cadence

**This is the structural hazard.** Every module cadence in the census is a `--countdown` decremented **per call**, never a `world.WorldTick % N` and never a tick-stamp comparison. Withhold a module and its "interval" stretches by the withhold factor. Only three pieces of state are genuinely tick-stamped and therefore skip-safe: `CaptureCoordinator.defenderBookings` (`:441`) and `lastFloorRequestTick` (`:400`); `LayeredDefence.assignedAtTick` (`:181`); `EngineerRouteOpen.missionStartTick` (`:279`).

Ranked by severity:

1. **`HelicopterSquadBotModule` — a hard correctness coupling, not a tuning preference.** `PruneSquads` (`:748`) runs on the **5-tick** branch, and its header comment (`:742-747`) states explicitly that pruning only on the slow `ScanInterval` **is not enough — a squad state tick that reaches a Disposed member throws.** Additionally `transportTasks` (`:388`) is a `Loading→Delivering→Unloading→Returning` FSM advanced only in `AdvanceTransportTasks` on the 100-tick branch, and `idleTicks` (`:368`) counts consecutive *calls* of a function documented at `:548-549` as deliberately running every tick so the gate counts game ticks (`EvacuateIdleTicks: 500`, `TransportIdleEvacuateTicks: 900`).
2. **Ledger-refresh coupling.** `LaneAmbush` (TTL 250, refresh only inside `CommitAndOrder` at `:421`, cadence 100), `EngineerRouteOpen` (TTL 400, refresh only inside `TickActiveMission` at `:286`/`:290-292`, cadence 100), `CaptureCoordinator` (TTL 600, `ReconcileGuardCommitments` at `:1121` only inside the 75-tick capture pass), `PoiGarrison` (TTL 250, cadence 100). **Withhold any of them past its TTL and its units silently become free for other writers while the module still believes it owns them** — reproducing the "derricks ignored" bug the ledger was written to fix (`ai.yaml:71-76`, `PoiGoalGuard.cs:6-17`).
3. **`MountedTransportBotModule` — a polled 4-state FSM per carrier** (`:144`, `:370-478`). Arrival, unload-completion and return-completion are detected **only on a scan** (`:400`, `:468`). Skip the scan and a carrier that reached its drop-off never unloads. `LoadingTimeoutTicks: 1500` is measured in world ticks but only *observed* on a scan, so the effective timeout quantises to the attention interval.
4. **`PoiOffensiveBotModule` counts in *evals*, not ticks** — `LosingStreak`, `FillHoldEvals` (capped by `MaxAdvanceHoldEvals`), `ReadvanceHold`, `RetreatSustainEvals`. Variable attention silently retunes every force-preservation and damper constant. Separately, `firesHeldThisEval` reconciliation (`:2885-2895`) **only runs if the module runs** — a skipped eval can strand a rocket battery in `HoldFire` permanently.
5. **Squad FSM one-shot states** — `AirFleeState:224`, `HelicopterReturnState:912` (and, in the dead ground layer, `GroundUnitsFleeState:293`) transition after exactly one tick. They issue their orders, then need a tick purely to advance. Any scheduler must still tick FSMs it is not "attending to", or model attention at the squad level rather than the module level. `HelicopterApproachState.stuckTicks` (`:457`), `HelicopterAttackRunState.attackTicks` (`:673`) and `HelicopterWithdrawState.withdrawTicks` (`:764`) all count **ticks-of-attention**, not world ticks.
6. **`BaseBuilderBotModule`** — `failRetryTicks` (`StructureProductionResumeDelay = 1500`) and `checkForBasesTicks` (`CheckForNewBasesDelay = 1500`) are per-call decrements, so the fail backoff and water re-check stretch by the skip factor.

### 6.4 Determinism the scheduler must preserve

From `DOCS/reference/influence-stack.md:101-107`:

- **`:103` — zero `SharedRandom`/`LocalRandom` draws in the stack.** Layers self-stagger with *distinct deterministic offsets* (BeliefStore 0, DangerFieldLayer `Interval/3`, ControlField `Interval/2+1`). Nav/scoring is integer walks over fixed candidate orders with iteration-order tie-breaks.
- **`:104` — byte-identity when flags off.** Every consumer flag defaults off/inert so `@stable` is byte-identical.
- **`:105` — do NOT gate on `InfluenceStack.Participates`**; since the 0802 promotion it returns true for `experimental`, `stable` *and* humans (`InfluenceStack.cs:43-52`). Use an explicit bot-type conjunct (`CommitOnOrderMath.ShouldCommitShared`, `PoiGoalGuard.cs:300`). The doc warns that in-file comments still citing the `participates` double-gate are *"stale and load-bearing-false."*

**A live tension:** several existing schedule points **do** draw RNG — `SquadManagerBotModule.cs:214-215` and `:206-216` seed stagger from `World.LocalRandom.Next(...)`; so do `CaptureCoordinatorBotModule:429-430`, `LayeredDefenceBotModule:208`, `MountedTransportBotModule:302`, `PoiOffensiveBotModule:1011`, `PoiGarrisonBotModule:187`, and `BaseBuilderQueueManager:103-106`. Bot decisions are seed-reproducible (`architecture.md:415-417`, `World.cs:213-214`), so this is currently *consistent*, not broken — but any scheduler subsuming these cadences must either preserve the draw order exactly or replace it with deterministic offsets.

### 6.5 Two channels a scheduler at the order layer would not see

Restating §1.1 and §1.4 because it bears directly on sizing: `HelicopterSquadBotModule.cs:1722` (`QueueActivity(RotateToEdge)`), `LaneAmbushBotModule.cs:456-462/479-483` (direct `ExternalCondition` grant), and the entire unit-level layer — `StancePositioningExecutor` (`:414`, every 30 ticks, on every `^Combatant` under `@experimental` **and every human-owned combatant**), `CohesionSlotMemory` (`:227`), `AutoSeekSupplies` (`:112`, every 40 ticks, on **every soldier, bot- and human-owned alike**), `GarrisonManager` (`:573+`, direct trait manipulation). Gating orders does not gate these.

### 6.6 Grouped vs per-actor orders

`CaptureCoordinator:1309/1416`, `LaneAmbush:439`, `PoiGarrison:479` and most `PoiOffensive` sites issue **one** order for N units via `groupedActors`. `LayeredDefence`, `EngineerRouteOpen`'s screen, `HelicopterSquad`'s passengers and all squad states issue **N** orders in a loop. A "one order per attention slot" model treats these very differently — the same tactical act costs 1 slot or 8 depending on which module performs it.

---

## 7. What I could not establish

- **The effective `NetFrameInterval` in skirmish/autotest runs.** `Session.cs:221` defaults to 3; whether the local/echo path overrides it was not traced. This scales the ≥2-tick order-resolution floor of §3.2.
- **Whether any `IModifyGroupOrder` implementation** (`UnitOrders.cs:405-411`, e.g. `CohesionMoveModifier`) performs conflict-relevant rewriting. The seam exists and is deterministic per grouped order; the implementations were not read.
- **The exact TTL passed at `HelicopterSquadBotModule.cs:1287`** (a local variable, not traced to its assignment).
- **Whether `BaseBuilderBotModule` actually places anything in a live WW3MOD match.** Confirmed attached, un-gated for both bots, ticking every frame; the SR's `Production@Local` queue wiring was not read and no game was run.
- **An exhaustive audit of the ~57 `IsIdle` call sites.** Sampled (`SquadManagerBotModule.cs:430`) and reasoned from re-fire intervals; an individual site may gate more strictly than described.
- **Whether `PoiOffensiveBotModule`'s eval-counting fields have any tick-stamped backstop** beyond `MissionCommitmentWindowTicks`. The field list was enumerated from the `Axis` class (`:840-901`) but each counter's update site was not individually traced.
