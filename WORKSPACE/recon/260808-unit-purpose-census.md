# Unit-purpose census — why an idle technician garrisons a civilian house

**Researched against `main` @ `8d0ff18b`** (`git status -sb`: `main...origin/main [ahead 37]`, `git rev-list --count HEAD..@{u}` = 0, tree clean apart from untracked scratch). Static analysis only — **no game runs, no autotests, no code changes**. Every claim carries a `file:line`, and every cited line was read in this session rather than recalled from a doc.

**Triggering observation.** An `@experimental` bot spawning left-middle sent several of its opening-minutes **technicians** into a **neutral civilian house in the bottom-left** and garrisoned them. There were **no enemies anywhere on the map**, which in WW3MOD's opening is always true.

**Companion document.** `WORKSPACE/recon/260807-order-source-census.md` (researched @ `9b39ebf1`) is the order-source map this builds on. ⚠️ **Its `ai.yaml` line numbers have drifted** — that file gained ~21 lines between `9b39ebf1` and `8d0ff18b`. Example: `GarrisonBotModule@defenses` is cited there as `ai.yaml:710`; it is **`ai.yaml:731`** today. Every `ai.yaml` citation below is re-verified at `8d0ff18b`. Its C# citations spot-checked clean.

---

## 0. Headline findings

1. **The garrison order came from `GarrisonBotModule.cs:203`, and there is no enemy-proximity or danger gate on that path — not a weak one, not a mis-tuned one. There is none.** The only threat-aware code in the module (`:151-159`) is a **sort comparator**, never a filter. With zero enemies it degenerates to an arbitrary ordering (§1.3). This is not a belief/danger field misreading "dangerous"; it is the absence of any such read.
2. **The technician is uncontested precisely because every other module excluded it.** `tecn` is on the exclusion list of every POI/defence/ambush/squad module in `ai.yaml`. `GarrisonBotModule@defenses` (`ai.yaml:731-741`) is the **only** bot module that names no exclusion list at all, and its eligibility fallback admits any `PassengerInfo` holder (`GarrisonBotModule.cs:260-272`). The garrison module wins by being the last predicate standing (§4).
3. **The idle sink is not garrison — garrison is a symptom. The real sink is "stand still at the rally point forever," and it exists because opening play has no owner.** `LayeredDefenceBotModule.cs:28-29` documents its own no-contact behaviour as a hand-off: *"this module does nothing — existing SquadManagerBotModule handles opening play."* `SquadManagerBotModule` **does not handle it** — every shipped instance sets `IgnoreGroundUnits: true` (`ai.yaml:1190, 1281, 1734, 1747`) and `continue`s past ground units without claiming (`SquadManagerBotModule.cs:329-336`), explicitly deferring to `PoiOffensiveBotModule`. `PoiOffensiveBotModule` in turn returns **before building its free pool** when no enemy POI scores (`:1261-1272`, pool built at `:1287`). **The hand-off chain terminates in a dangling reference.** (§3.4)
4. **A garrisoned bot unit never comes out.** No bot module issues `Unload` at a garrison building — all four `Unload` sites in `BotModules/` target a carrier the issuing module owns a task for (`HelicopterSquadBotModule.cs:1253,1359`; `MountedTransportBotModule.cs:407,462`). So this is not a temporary parking mistake; it is a **permanent removal of the unit from the match** until the house dies (§2.4).
5. **A technician is the worst possible unit to lose this way.** `tecn` is a **consumable** — `ConsumedByCapture: true` (`infantry.yaml:903`, via `^CapturesNeutralBuildings`) — with `UnitLimits` of 3 (`ai-america.yaml:41`). Technician availability, not coordinator logic, is the binding constraint on the entire capture game (`game-model.md:35`). Losing two into a house can end capturing for the match (§5).

---

## 1. Which module issued it, and what is its gate? (Q1)

### 1.1 Ruling the candidates in and out — VERIFIED

`Grep` for `"EnterTransport"` / `"EnterGarrison"` across `engine/` returns **exactly four bot-module sites**. There is no `EnterGarrison` order string anywhere in the engine; garrisoning a building is done with `EnterTransport` against the building actor (`GarrisonBotModule.cs:202` says so in comment).

| Site | Target | Verdict |
|---|---|---|
| **`GarrisonBotModule.cs:203`** | a **building** (`Target.FromActor(building)` from `ActorsHavingTrait<GarrisonManager>()`) | ✅ **This is the one.** Only bot site that targets a building. |
| `MountedTransportBotModule.cs:268` | `carrier` (capture ferry) | ruled out — vehicle |
| `MountedTransportBotModule.cs:621` | `carrier` | ruled out — vehicle |
| `HelicopterSquadBotModule.cs:1075` | `transport` (helicopter) | ruled out — aircraft |

The three named suspects are all cleared:

- **`PoiGarrisonBotModule`** — *"garrison"* here means **hold a POI**, not enter a building. Its only order is `bot.QueueOrder(new Order("AttackMove", null, Target.FromCell(world, g.PoiCell), false, groupedActors: units))` (`PoiGarrisonBotModule.cs:479`). It also early-returns on `targets.Count == 0` (`:251-256`, `RetireAll("no-held-pois")`), and it **excludes `tecn`** (`ai.yaml:656`). **Cleared.**
- **`LayeredDefenceBotModule`** — issues `AttackMove` only (`:504`, `:639`); excludes `tecn` (`ai.yaml:1010`); and hard-returns with no contact (`:322-323`). **Cleared.**
- **`LaneAmbushBotModule`** — `AttackMove` + `SetUnitStance` only; excludes `tecn` (`ai.yaml:700`). **Cleared.**

### 1.2 The exact predicate that let it fire — VERIFIED

`GarrisonBotModule.BotTick` (`:125-224`). The full gate, in order:

```csharp
if (--scanCountdown > 0) return;              // :127
scanCountdown = Info.ScanInterval;            // :130   ScanInterval: 200 (ai.yaml:733) = 12.0 s
```

**`scanCountdown` is a default-initialised `int` = 0** (`:65`). First call: `--scanCountdown` → `-1`, `-1 > 0` is false ⇒ **the module fires on the bot's very first tick.** There is no stagger and no randomised offset anywhere in `Initialize()` (`:101-123`) — unlike `CaptureCoordinator` (`:429-430`), `LayeredDefence` (`:208`) and `MountedTransport` (`:302`), which all randomise. This directly matches "within the opening minutes."

Building set (`:141-145`):
```csharp
world.ActorsHavingTrait<GarrisonManager>()
  .Where(a => !a.IsDead && a.IsInWorld
    && (a.Owner == player || a.Owner.RelationshipWith(player) == PlayerRelationship.Neutral)
    && (a.Location - baseCenter).Length <= Info.MaxGarrisonRadius)   // MaxGarrisonRadius: 25 (ai.yaml:734)
```

Infantry set (`:162-172`):
```csharp
world.ActorsHavingTrait<Mobile>()
  .Where(a => a.Owner == player && a.IsIdle && !a.IsDead && a.IsInWorld
    && IsGarrisonEligible(a)
    && !IsClaimedByOtherModule(a)
    && (!LedgerActive || !goalGuard.Ledger.IsCommitted(a, world.WorldTick)))
```

Pairing + order (`:179-223`): first building with `Cargo.HasSpace(1)`, nearest infantry passing `CanEnter` (cargo-type match, `:229-236`), then `bot.QueueOrder(new Order("EnterTransport", infantry, Target.FromActor(building), false))` at `:203`. Capped at `MaxOrdersPerTick: 2` (`ai.yaml:735`) — **two per 12-second scan, which matches "several technicians" over the opening minutes.**

### 1.3 Is there an enemy-proximity or danger condition at all? — **VERIFIED: NO.**

I read the entire 293-line file. The complete set of terms in both predicates is listed above. **Not one of them references an enemy, a threat, a danger field, a belief store, an influence map, a frontline, or a POI.**

The single threat-aware construct is `PrioritizeExposed` (`ai.yaml:736`, `true`):

```csharp
if (Info.PrioritizeExposed && threatMap != null)
{
    garrisonableBuildings.Sort((a, b) => {
        var threatA = threatMap.GetThreat(a.Location, player);
        var threatB = threatMap.GetThreat(b.Location, player);
        return threatB.CompareTo(threatA);
    });
}                                                          // :151-159
```

This is `List<T>.Sort` — a **reordering, not a filter**. No building is ever removed. `ThreatMapManager` **is** attached (`world.yaml:283`) so `threatMap != null` and the branch is live, but `GetThreat` (`ThreatMapManager.cs:197-215`) sums `Valued` actors with `AttackBase`/`AutoTarget` inside the cell radius. **With no enemies on the map every building scores identically**, the comparator returns `0` for every pair, and `List.Sort` (unstable introsort) leaves an arbitrary — though deterministic — order. So `PrioritizeExposed: true` does not merely fail to gate; **with zero enemies it degenerates to picking an essentially arbitrary house.**

**Answer to "is there a gate that fired anyway?"** — no. There is nothing to have fired. This is not an influence-stack misread, so `DOCS/reference/influence-stack.md` is **not** implicated; I did not need to open it, and the invariants there are untouched by this finding.

---

## 2. Is the house a sensible garrison target? (Q2)

### 2.1 What makes a building eligible — VERIFIED

Three conditions, all in `:141-145` + `:184-197`:

1. **Has `GarrisonManager`.** In WW3MOD that is `^CivBuilding` — i.e. **every civilian house** (`civilian.yaml:63`) — plus three defence structures (`structures-defenses.yaml:125, 218, 306`).
2. **Owned by us, or Neutral.** Map civilian buildings are Neutral, so they qualify. (VERIFIED that the predicate admits Neutral; that the specific house was Neutral-owned is INFERRED from the screenshot + `^CivBuilding` being the only civilian template carrying `GarrisonManager`.)
3. **Within `MaxGarrisonRadius: 25` cells of `baseCenter`**, plus a `Cargo.HasSpace(1)` check and the `CanEnter` cargo-type match (`Passenger.CargoType` ∈ `Cargo.Types`). `^CivBuilding` sets `Cargo: Types: Infantry, MaxWeight: 10` (`civilian.yaml:52-55`); `^TECN` inherits `Passenger: CargoType: Infantry` from `^Infantry` (`infantry.yaml:85-86`). **The technician passes.**

### 2.2 `baseCenter` is a frozen random building — VERIFIED

```csharp
var bases = world.ActorsHavingTrait<Building>().Where(a => a.Owner == player).ToList();
baseCenter = bases.Count > 0 ? bases.Random(world.LocalRandom).Location : player.HomeLocation;   // :114-120
```

`Initialize()` is guarded by `if (initialized) return;` (`:103-104`) and sets `initialized = true` (`:122`), so **`baseCenter` is sampled once, at the first scan, and never updated.** In WW3MOD the bot's building set at t=0 is essentially the Supply Route, so this lands ~25 cells around the SR — the bot's own rear. The bottom-left house in the screenshot is comfortably inside that from a left-middle spawn.

Note also that `bases.Random(world.LocalRandom)` is an **RNG draw inside a module that no longer needs one** — irrelevant when the bot owns one building, latent divergence-in-behaviour if it ever owns more.

### 2.3 Does it distinguish rear civilian clutter from a lane- or POI-covering building? — **VERIFIED: NO.**

There is no reference in the file to `PoiMap`, `ControlField`, `InfluenceMap`, `CrossingMap`, lanes, avenues, or frontline. A building is eligible **iff** it has `GarrisonManager`, is friendly-or-neutral, and is within 25 cells of a frozen random own-building location. A rear civilian house 25 cells behind the SR and a house overlooking a contested crossing are indistinguishable to this module — and by §1.3, with no enemies the sort cannot even prefer the latter.

The trait's own `[Desc]` says *"Sends idle infantry to garrison friendly defense structures and nearby buildings"* (`:18`) and the YAML comment says *"Garrison defense structures with infantry for base defense"* (`ai.yaml:730`). **Neither is what the code does**: because `GarrisonActorTypes` is unset the unit side is unrestricted, and because `^CivBuilding` carries `GarrisonManager` the building side is dominated by civilian clutter, not defence structures. The module is named and documented for a job narrower than the one it performs.

### 2.4 The unit never comes back — VERIFIED

I grepped all of `BotModules/` for `"Unload"`. Four sites, all targeting a carrier the issuing module holds a task for: `HelicopterSquadBotModule.cs:1253` (`transport`), `:1359` (`h`), `MountedTransportBotModule.cs:407` and `:462` (`carrier`). **No bot module ever unloads a garrison building.** `GarrisonBotModule` itself contains no ungarrison path — its only outbound action is `EnterTransport`.

Inside `GarrisonManager`, `IdleRecallTicks` (`:66`) and `SuppressionRecallThreshold` (`:98`) drive `RecallToShelter` (`:390`), which moves a soldier **from a firing port back into the shelter** — i.e. deeper into the same building. Exiting requires an `Unload` order (`GarrisonManager.cs:1253, 1338`) which for a bot is never issued, or `EjectOnDeath: True` (`civilian.yaml:61`) when the building dies.

Meanwhile the *registries* release the unit even though the unit is physically stuck: `ReleaseFinishedClaims` (`:245-258`) drops the `BotBlackboard` claim once `!a.IsInWorld`, and the ledger commit expires on TTL. The code comments this deliberately (`:210-212`, *"it leaves the world, so it's unorderable anyway"*). Consequence: **the bot's bookkeeping shows a free unit; the battlefield shows nothing.** For any future accounting layer this is a silent leak.

---

## 3. What decides a soldier's purpose today, end to end? (Q3)

### 3.1 Role assignment — VERIFIED

`UnitRoleResolver` (`world.yaml:351`, `Traits/World/UnitRoleResolver.cs`) classifies **every actor name once at map load** (`:189-208`) into a 9-value closed taxonomy (`:37-48`). It is a pure function of the ruleset (`:319-384`), cached `Dictionary<string, UnitRole>` — **per actor *type*, not per actor instance.** There is no per-unit, per-situation role.

The cascade is first-match: override → air → `CapturesNeutral` → logistics → ShortRangeAD → IndirectFire → Recon → `HasCargo` → armed+mobile ⇒ `MainBattle` → `None`. `tecn` matches at step 3 (`:349-350`) ⇒ **`CaptureSpecialist`**.

Modules opt in via a per-module `UseUnitRoles` flag — set `true` on 20 blocks in `ai.yaml` (`:99, 370, 659, 701, 883, 927, 996, 1195, 1285, 1530, 1603, 1642, 1665, 1678, 1697, 1716, 1737, 1750`, plus two comment lines). **`GarrisonBotModule@defenses` (`ai.yaml:731-741`) has no `UseUnitRoles` field, and `GarrisonBotModuleInfo` (`GarrisonBotModule.cs:19-54`) declares none.** It is the one live unit-consuming module entirely outside the role system.

### 3.2 Role → destination — VERIFIED

A role is only a **filter on a recruitment pool**. It never itself produces a destination. Each module converts its filtered pool into a destination by its own means:

| Module | Role filter | Destination source | Behaviour with no enemy contact |
|---|---|---|---|
| `PoiOffensiveBotModule` | `MainBattle \|\| IndirectFire`, `!IsTroopCarrier` (`:2380`) | enemy POI axes | **`targets.Count == 0` ⇒ `RetireAllAxes("no-targets")`, `return` (`:1261-1272`)** |
| `PoiGarrisonBotModule` | same shape (`:430-431`) | POIs **we already hold** | `targets.Count == 0` ⇒ `RetireAll("no-held-pois")`, `return` (`:251-256`) |
| `LayeredDefenceBotModule` | `MainBattle` only, `!IsTroopCarrier` (`:673-677`) | contested cells from `InfluenceMap` | **`contestedCells.Count == 0 && !manThreat` ⇒ `return` (`:322-323`)** |
| `LaneAmbushBotModule` | `MainBattle \|\| IndirectFire` + `CanHostAmbush` | lane posts | early-return on empty viable set |
| `CaptureCoordinatorBotModule` | `CaptureSpecialist` | `PoiMap.GetCaptureTargets` | issues **nothing** (§5.2) |
| `ScoutBotModule` | actor-name allowlist (`humvee`/`btr`) | unexplored map | the one module that acts with no enemy known |
| **`GarrisonBotModule@defenses`** | **none — any `PassengerInfo` holder** | **any `GarrisonManager` building in radius** | **fires unconditionally** |

**Every offensive/defensive consumer is gated on enemy knowledge. The garrison module is the only unit-consuming module that is not.** That asymmetry is the whole bug: on an empty map, the set of modules willing to take a unit narrows to exactly one, and that one puts the unit in a box.

### 3.3 What happens when no role wants it — the forward-staging near-miss — VERIFIED

`PoiOffensiveBotModule` does have a reserve handler: `StageFreePool` (`:2180-2280`), which walks genuinely-idle uncommitted units to a forward staging anchor — the comment at `:1464-1466` states the intent exactly: *"instead of leaving it idle at the SR clogging the road to the front."* It is enabled (`ForwardStagingEnabled: true`).

**But it is called at `:1467`, and the `targets.Count == 0` early return is at `:1261-1272`.** With no scoreable enemy POI the method returns ~200 lines before `StageFreePool` is reached. It also self-gates on `!stagingAnchor.HasValue` (`:2201-2202`), which requires a `ControlField` and a resolved rally cell.

So the one piece of code written to solve "idle at the SR" is unreachable in exactly the situation it was written for. (INFERRED-with-high-confidence that this is the live early-game state; whether a given map scores zero POIs at t=0 needs a run — see §7.)

### 3.4 **Name the idle sink — VERIFIED**

> **The idle sink is: the unit stands at the rally point and is never ordered again, because opening play has no owner. Idle-garrison is not the sink — it is the one thing that opportunistically drains from it, and it drains in the wrong direction.**

The proof is a three-link hand-off chain that terminates in nothing:

1. `LayeredDefenceBotModule.cs:28-29` (file header): *"When the frontline is empty (no contact), this module does nothing — **existing SquadManagerBotModule handles opening play**."* Enforced at `:322-323`.
2. `SquadManagerBotModule.cs:329-336`: `else if (Info.IgnoreGroundUnits) { /* this manager does not own ground units — skip without claiming ... so the PoiOffensiveBotModule sees them as a free pool */ continue; }`. Set `true` on **all four** shipped instances (`ai.yaml:1190, 1281, 1734, 1747`). SquadManager explicitly hands ground play to PoiOffensive.
3. `PoiOffensiveBotModule.cs:1261-1272`: with `targets.Count == 0`, retire and `return` — **before** `BuildFreePool()` at `:1287` and before `StageFreePool` at `:1467`.

**A ⇒ B ⇒ C ⇒ ∅.** No module in the census is a catch-all. I searched for one and did not find it: no reserve pool, no unassigned-unit handler, no default assignment. `SquadManagerBotModule.AssignRolesToIdleUnits` is the only engine-level idle-unit assigner and it is disabled for ground by the flag above. The `BotBlackboard` task API (`PostTask`/`ClaimTask`/`GetOpenTasks`) that could have served as one has **zero callers** — dead code (order-source census §3.4, re-confirmed).

**Is idle-garrison the sink "by design or by accident"?** By accident, and demonstrably so. The design intent is written down in three places and all three describe something else: the trait `[Desc]` says *defense structures* (`GarrisonBotModule.cs:18`), the YAML says *"for base defense"* (`ai.yaml:730`), and `PoiOffensiveBotModule.cs:1464-1466` says the idle reserve should *stage forward*. Nothing anywhere says "park spare infantry in civilian houses." The behaviour is an emergent product of two independent omissions — an unset `GarrisonActorTypes` and an absent enemy gate — meeting a hole where opening play should be.

### 3.5 The unit-level layer does not fill the gap — VERIFIED

`^Combatant` (`defaults.yaml:20, 26-32`) grants `CohesionSlotMemory` and `StancePositioningExecutor`. Neither is a purpose:

- `CohesionSlotMemory.TickIdle` only returns a unit to a slot **previously assigned by a grouped order** (`:204-205` no-ops without a slot). A never-ordered unit is inert.
- `StancePositioningExecutor.TickIdle` (`:277`) needs `ComputeThreatBearing`, which returns null below `MinThreatIntensity: 40` (`:469`, `defaults.yaml`) — **with no enemy it queues no `Move` at all** (`:364-376`). Even when it does fire it is leashed to `LeashRadius: 4` cells: a cover shuffle, not a destination.

**And a technician has neither trait.** `^TECN` (`infantry.yaml:2189`) → `^ArmedCivilian` (`:344`) → `^CivInfantry` (`:309`) → `^Infantry` (`:2`), which inherits only `^ExistsInWorld`, `^GainsExperience`, `^SpriteActor`, `^GlobalBounty`, `^SelectableCombatUnit`, `^EffectsWhenDamagedInfantry`, `^PlayerHandicaps` (`:3-14`). `^Combatant` is inherited exactly once in the infantry file — by `^CamoSoldier` (`:256-258`), the line-infantry base. `^Soldier` (`:167`), which carries `AutoSeekSupplies` (`:221`), is likewise not in the technician's chain. `^TECN` even strips `-Wanders:` (`:2193`).

**A technician therefore has zero autonomous behaviour of any kind.** It is 100% dependent on a module ordering it — which makes it the purest possible test case for "what is this unit's purpose," and it fails that test.

*(Aside, not pursued: `^TECN` removes `Wanders` but **not** `ScaredyCat`, which `^CivInfantry:334` grants. `ScaredyCat.cs:108` participates in order handling. Bot-owned technicians therefore carry a panic behaviour. The 260807 census §1.4 records `ScaredyCat`/`Wanders` as being "on no bot-owned unit today" — that is **wrong for `ScaredyCat` on `tecn`**. Flagged, not investigated.)*

---

## 4. How many modules can claim the same soldier, and who wins? (Q4)

### 4.1 Situating garrison in the two-layer map

Per the 260807 census §1.1 there are two order layers, and activity-queueing traits produce no `Order` at all. `GarrisonBotModule` sits squarely in the **module/order layer** — `bot.QueueOrder` at `:203`, funnelled through `ModularBot.QueueOrder` and drained ~1/5 per tick. It is fully visible to an order-layer scheduler. Its *effect*, however, hands the unit to `GarrisonManager` (`civilian.yaml:63`), which is a **unit-level, order-free FSM** (`GarrisonManager.cs`, direct trait manipulation). So the act of garrisoning is a **one-way transfer from the order layer into the activity layer** — the last order a bot ever issues to that unit.

### 4.2 Who else could have claimed the technician — VERIFIED: nobody

`tecn` appears in `ai.yaml` in these lists at `8d0ff18b`:

| Module block (line) | List (line) | Effect on `tecn` |
|---|---|---|
| `CaptureCoordinatorBotModule@experimental.tecn` (94) | `CapturingActorTypes` (96) | **INCLUDE** |
| `PoiOffensiveBotModule@experimental` (235) | `ExcludeUnitTypes` (366) | exclude |
| `PoiGarrisonBotModule@experimental` (643) | `ExcludeUnitTypes` (656) | exclude |
| `LaneAmbushBotModule@experimental` (686) | `ExcludeUnitTypes` (700) | exclude |
| `LayeredDefenceBotModule@experimental` (973) | `ExcludedActorTypes` (1010) | exclude |
| `EngineerRouteOpenBotModule@experimental` (1024) | `ExcludedActorTypes` (1034) | exclude |
| `MountedTransportBotModule@experimental` (1087) | `PassengerTypes` — **absent** | not a routine passenger |
| `SquadManagerBotModule@*.fixedwing` (1179, 1270, 1721, 1739) | `ExcludeFromSquadsTypes` (1191, 1282, 1735, 1748) | exclude |
| `CaptureCoordinatorBotModule@stable.tecn` (1527) | `CapturingActorTypes` (1529) | **INCLUDE** |
| `PoiOffensive/PoiGarrison/LaneAmbush@stable` (1586/1628/1654) | `ExcludeUnitTypes` (1602/1638/1664) | exclude |
| `LayeredDefenceBotModule@stable` (1705) | `ExcludedActorTypes` (1719) | exclude |
| **`GarrisonBotModule@defenses` (731)** | **no list of any kind** | **INCLUDE by omission** |

So the conflict-resolution question has a degenerate answer here: **there was no conflict.** Two modules can legitimately want a technician — `CaptureCoordinator` and `Garrison` — and per the 260807 census §2.3 they arbitrate through *different registries* (`PoiGoalGuard` vs `BotBlackboard`), which is a real latent hazard. But it did not bite in this incident, because CaptureCoordinator had issued **no** order and taken **no** claim (§5.2), leaving the technician clean in both registries.

### 4.3 YAML declaration order — VERIFIED as not load-bearing here

Per the 260807 census §3.3, the winner of a genuine conflict is whichever module is declared **later** in `ai.yaml`. `GarrisonBotModule@defenses` at `:731` is declared *before* `SupplyFollower` (`:744`), `LayeredDefence` (`:973`), `EngineerRouteOpen` (`:1024`) and `MountedTransport` (`:1087`) — so it would *lose* a same-tick contest with any of them. It won here purely because all of them exclude `tecn`. **Note this cuts against a tempting fix**: reordering YAML would not have prevented this, because nothing was contesting.

---

## 5. Technicians specifically (Q5)

### 5.1 Intended role — VERIFIED

`tecn` is a **`CaptureSpecialist`** (`UnitRoleResolver.cs:45, 347-350`, via `Captures` with type `building-neutral` from `^CapturesNeutralBuildings`, `infantry.yaml:2192`). Its purpose is to capture neutral tech/income structures — `CapturableActorTypes: oilb,bio,miss,fcom,hosp,logisticscenter` (`ai.yaml`, `@experimental.tecn` block at `:94`). **Civilian houses are not on that list**, which sharpens the finding: the technician entered a building class its own doctrine explicitly does not care about.

It is `Cost: 250` (`infantry.yaml:2205-2206`), **consumed on successful capture** (`infantry.yaml:903`), and limited to 3 (`ai-america.yaml:41`, mirrored in `ai-russia.yaml`).

Two YAML inconsistencies noticed in passing, neither investigated: the `Buildable.Description` says *"Unarmed"* (`infantry.yaml:2204`) but `^ArmedCivilian` grants `Armament: Weapon: Pistol` + `AttackFrontal` (`:349-351`) and `^TECN` does not remove them — so a technician **is** armed, which is what makes it visible to `AttackBase`-shaped pools like `CaptureCoordinator.FindIdleSupportersNear`.

### 5.2 Do technicians have a distinct purpose, or fall through to generic infantry? — **Distinct on paper; none at all in practice when there is no capture target.**

`CaptureCoordinatorBotModule` builds `idleCapturers` (`:538-546`) and dispatches only through `QueueCaptureOrdersFromPoiMap` (`:978-1015`). If the target list is empty the `foreach` body never executes and the method returns; if targets exist but no capturer passes `CaptureManager.CanTarget`, it `continue`s (`:1009-1010`). **In neither case is any order issued** — no `Move`, no `Stop`, no park, no rally. The module's only capturer `Move` is `RetreatCapturerWhenDone` (`:1166-1176`), which fires only after a commitment is released because a target was captured or destroyed — i.e. never for a technician that was never dispatched.

So a technician with no capture target is left **`IsIdle`, standing where it arrived, holding no claim in either registry** — the exact state `GarrisonBotModule` recruits from at `:162-172`.

Compounding it: technician *production* is not demand-gated the same way the coordinator's own top-up is. `MaintainTecnFloor` sits behind `CaptureTargetExists()` (`:753`), but the shared `UnitBuilderBotModule` lottery lists `tecn.america: 500` in `UnitsToBuild` (`ai-america.yaml:12, 68`) independent of capture demand. **INFERRED** (not run): the bot fields technicians in the opening whether or not anything is capturable, and they idle from the moment they arrive.

### 5.3 `test-tecn-ride` — read in full

`tools/autotest/scenarios/test-tecn-ride/test-tecn-ride.lua` (59 lines). One USA experimental bot with one TECN + one bradley near its SR; a **neutral oil derrick ~39 cells east**, past the 12-cell ferry gate (`TransportCaptureMinDistanceCells: 12`). It pins the full ferry-capture chain inside a 100-second `TestHarness.AssertWithin` via four latches: `mounted` (`Carrier.HasPassengers`, `:37`), `delivered` (within `DROP = 6` cells of the derrick, `:40-44`), `dismounted` (`delivered and not Carrier.HasPassengers`, `:49`), and passes only on `dismounted and not Derrick.IsDead and Derrick.Owner.Name == "USA-bot"` (`:52-54`). Fail-fast if the carrier dies (`:34`). The header (`:16-19`) records that it was tightened specifically so a carrier that arrives but never unloads can no longer ship green.

**Why this is directly relevant.** The scenario is the *positive* control for exactly the failure observed: it is the only place that pins "a technician has a purpose and is executing it." And note **what it has to construct to get there** — it hand-places a capture target 39 cells away and hand-places the carrier. It proves the ferry-capture chain works *given a target*; it says nothing about, and cannot catch, the state where no target exists. **There is no scenario pinning what a technician does with nothing to capture** — which is why this shipped unnoticed. (VERIFIED that this scenario does not cover it; I did **not** enumerate every scenario directory to prove no other test covers it — see §7.)

---

## 6. Where the code contradicts the docs

Per the brief, contradictions are findings. Four, all **code-wins**:

1. **`LayeredDefenceBotModule.cs:28-29` names `SquadManagerBotModule` as the owner of opening play. It is not, and has not been since `IgnoreGroundUnits: true` went on all four instances** (`ai.yaml:1190, 1281, 1734, 1747`; skip at `SquadManagerBotModule.cs:329-336`). This is the single most consequential stale comment found — it is the reason the hole is invisible when reading either file alone. **Recommend correcting** (not done here; read-only task).
2. **`GarrisonBotModule.cs:18` `[Desc]`** — *"Sends idle infantry to garrison friendly defense structures and nearby buildings"* — and **`ai.yaml:730`** — *"Garrison defense structures with infantry for base defense"* — both misdescribe live behaviour. `GarrisonActorTypes` is unset so the unit side is not infantry-restricted, and `^CivBuilding` carries `GarrisonManager` so the building side is dominated by neutral civilian houses, not defence structures. (The `IsGarrisonEligible` comment at `:266-270` is *accurate* and even warns about this; the trait-level `[Desc]` was never updated to match.)
3. **`infantry.yaml:2204`** describes the technician as *"Unarmed"* while `^ArmedCivilian:349-351` gives it a pistol and `AttackFrontal`, un-removed by `^TECN`.
4. **`WORKSPACE/recon/260807-order-source-census.md` §1.4** records `ScaredyCat`/`Wanders` as *"on no bot-owned unit today."* `^TECN` strips `Wanders` (`:2193`) but not `ScaredyCat` (`^CivInfantry:334`), so bot technicians carry it. Minor; flagged for that doc's next revision.

Also worth recording for future readers: **`ai.yaml` line numbers in the 260807 census are stale by ~21 lines** (§ header above).

---

## 7. What I could not determine without a live run

- **Whether the observed match genuinely had zero scoreable POI targets**, versus targets that existed but failed `CanTarget`/pathing. Both routes reach the same idle state; the debug lines would separate them (`[exp-offense] reeval ... targets=0` at `PoiOffensiveBotModule.cs:1271`; `[exp-capture] poimap-scan ... targets=N` around `CaptureCoordinatorBotModule.cs:975`).
- **Which house was chosen and why.** With all threats equal the `List.Sort` outcome depends on the pre-sort enumeration order of `ActorsHavingTrait<GarrisonManager>()`. Deterministic, but not derivable statically.
- **Whether `baseCenter`'s `bases.Random` had more than one candidate** at first scan (it is a single sample, frozen for the match).
- **How many technicians were lost and over what interval.** `MaxOrdersPerTick: 2` per 12 s scan bounds it, but the observed count needs the run.
- **Whether any autotest other than `test-tecn-ride` touches technician idling.** I read `test-tecn-ride` in full but did not enumerate every scenario directory.
- **Whether this also happens to line infantry, not just technicians.** The predicate at `:162-172` admits any idle `PassengerInfo` holder with a matching cargo type, so `^CamoSoldier`-derived line infantry are structurally eligible too — but they are *contested* by PoiOffensive/LayeredDefence/LaneAmbush, which technicians are not. **INFERRED**: the same sink swallows line infantry whenever those modules are in their no-contact early-return, which is the same early game. Not observed; worth checking in a run before sizing a fix.

---

## 8. Judgement: small gate fix, or missing layer?

**A missing layer. Not a gate fix.** I want to argue this rather than assert it, because the gate fix is genuinely tempting and would look like it worked.

**The three-line fix exists and is real.** Setting `GarrisonActorTypes` on `ai.yaml:731`, or adding `ExcludeUnitTypes: tecn, ...`, or adding an enemy-proximity precondition to `GarrisonBotModule.BotTick`, would each stop *this screenshot* immediately. Any of them is cheap and low-risk. **I'd take one as a stopgap.** But each is a fix to the last predicate in a chain, and the chain is what's broken.

**Why it is not sufficient — the argument.**

1. **The observed behaviour is a symptom of absence, not of a wrong rule.** The bot did not decide to garrison. Nothing decided anything. A unit existed, every purposeful module declined it, and the one module with no declining condition picked it up. Remove that module and the technician does not acquire a purpose — it **stands still forever instead**, which is the same waste with worse optics: invisible rather than visible. The user's standard is *"every soldier should have a purpose and should be fulfilling it — walking to it or riding to it."* A gate fix moves the failure from *wrong purpose* to *no purpose*. It does not move it toward the standard.

2. **The hole is structural and is written down as a hand-off to something that does not exist.** §3.4: `LayeredDefence` → `SquadManager` → `PoiOffensive` → `return`. Three modules each correctly decline opening play on the belief that another owns it, and the named owner was disabled. This is not a tuning error in one predicate; it is an **unowned region of the state space**. And it is precisely the region the early game lives in — no contact, no scored POI — which is why it presents "within the opening minutes, always."

3. **The fix that was written for this is unreachable.** `StageFreePool` (`PoiOffensiveBotModule.cs:2180`) exists specifically to stop units *"leaving it idle at the SR clogging the road to the front"* (`:1464-1466`) — the same instinct the user is expressing. It is called at `:1467`, **after** the `targets.Count == 0` return at `:1261-1272`. Someone already identified the problem and put the remedy behind the very gate that fires in the problem case. That is strong evidence this is a layering fault, not an oversight in one module: the right idea is present and mis-placed.

4. **There is no seam to hang a fix on.** §3.4: no reserve pool, no unassigned-unit handler, no default assignment. The `BotBlackboard` task API that could have been that seam has zero callers. So "give every soldier a purpose" cannot be implemented as a config change to an existing owner — **there is no owner to configure.** Something has to be introduced.

5. **The accounting is silently wrong, which means this class of bug will recur unseen.** §2.4: a garrisoned unit is released from both registries while being permanently unorderable. Nothing in the bot can currently answer "how many of my units are contributing?" That is why several technicians could vanish into a house without any internal signal — and why the next variant of this will also be found by a human looking at a screenshot rather than by the system.

**What this implies for sizing** (stated as implication, not as a design — designing is a separate task): the missing piece is a **default assignment for units no purposeful module claims**, owning the no-contact state that §3.4 shows is unowned, plus enough accounting to make "unit with no purpose" an observable quantity rather than an inference from a screenshot. `StageFreePool` is the closest existing behaviour and is the natural thing to lift out from behind the target gate. The `PoiGoalGuard` ledger already knows which units are committed, so "committed vs. not" is available cheaply — what is missing is anyone who *acts* on "not."

**Recommended sequencing:** land the cheap gate fix to stop the visible bleeding — and record explicitly, in the commit message and in `WORKSPACE/PIPELINE.md`, that it is a stopgap for a missing layer, so it is not mistaken for a resolution. Then treat the unowned-opening-play layer as its own scoped piece of work. My concern with doing only the first is specific: it makes the screenshot go away, which removes the evidence that motivated the second.

---

### Verification summary

- **VERIFIED by reading code at `8d0ff18b`:** the order site and its complete predicate (§1.2); the absence of any enemy/danger term (§1.3); building eligibility and the frozen `baseCenter` (§2.1-2.2); the absence of any bot ungarrison path (§2.4); the role taxonomy and every module's no-contact early return (§3.1-3.2); the three-link hand-off chain terminating in nothing (§3.4); technicians lacking `^Combatant`/`^Soldier` (§3.5); the `tecn` inclusion/exclusion table (§4.2); `CaptureCoordinator` issuing no order without a target (§5.2); `test-tecn-ride` in full (§5.3); all four doc/code contradictions (§6).
- **INFERRED, not confirmed:** that the specific house was Neutral-owned; that the opening genuinely scores zero POI targets on the played map; that the bot fields technicians ahead of capture demand via the shared builder lottery; that line infantry fall into the same sink.
- **Requires a live run:** everything in §7.
