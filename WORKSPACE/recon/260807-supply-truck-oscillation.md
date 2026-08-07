# Recon: AI supply-truck oscillation (forward → back to SR → repeat)

**Researched against `main @ 9b39ebf1`** (`git status -sb`: `main...origin/main [ahead 13]`, 0 behind upstream). Static analysis only — **no autotest, batch or tournament run was performed**, so every claim below is read off code and YAML at that SHA. Claims that would need a live run to confirm are flagged in §6.

Read-only recon. No engine behaviour was changed.

---

## Verdict up front

**The manager's hypothesis — two order sources on different cadences fighting over the same actor — is REFUTED as the primary cause.** It is a real structural weakness (§1, §2) and it is why some *other* truck pathologies exist, but it is not what produces the observed back-and-forth.

**The oscillation is a single module fighting itself.** `SupplyFollowerBotModule.BotTick` contains two mutually exclusive branches over the same `foreach`, chosen every ~150 ticks (~6 s) by a stateless threshold:

- **forward** — `Move` to a follow cell near a friendly cluster (`SupplyFollowerBotModule.cs:353` / `:386`)
- **backward** — `Move` 12 cells toward the player's own Supply Route (`:331`)

and **the criterion that selects a cluster is positively correlated with the criterion that then rejects it.** Cluster choice is `OrderByDescending(c => c.AmmoNeed)` (`:310`) — the neediest cluster is the one that has been fighting, which is the one sitting in the highest believed danger, which is exactly what `ShouldEvacuate` (`:612-617`) rejects. The module therefore *systematically* picks the cluster most likely to immediately trigger its own retreat. Select → reject → retreat → re-select. That is a guaranteed limit cycle, and it needs no second order source at all.

The user's own words map onto the two branches exactly: *"they seem to go forward … and then they are ordered back towards the base/SR and that just repeats."* `:331` is literally a move toward the SR actor.

**Supply points: YES, buildable without a bot-brain rewrite** — see §5. The pull side already ships and already works.

---

## 1. Every code path that can order a `truk`

`truk` is the only supply truck actor; there are no faction variants (`CheckUnitRoleTable.cs:78` lists one entry; no `truk`-like name appears in `vehicles-america.yaml` / `vehicles-russia.yaml`).

### 1.1 Live writers

| # | Site | Order | Destination | Queued? |
|---|---|---|---|---|
| A1 | `SupplyFollowerBotModule.cs:331` | `Order("Move", …, false)` | 12 cells toward own SR | **cancels** |
| A2 | `SupplyFollowerBotModule.cs:353` | `Order("Move", …, false)` | follow cell near cluster | **cancels** |
| A3 | `SupplyFollowerBotModule.cs:377` + `:378` | two `Move`s | detour waypoint, then follow cell | `:377` cancels, `:378` queued |
| A4 | `SupplyFollowerBotModule.cs:386` | `Order("Move", …, false)` | follow cell (no detour) | **cancels** |
| A5 | `SupplyFollowerBotModule.cs:492` | `Order("Move", …, false)` | starving-infantry hunt target | **cancels** |
| B | `DropsSupplyCache.cs:246` | `QueueActivity(false, RotateToEdge)` | **map edge**, then sells | **cancels** |

A1–A5 are five mutually exclusive branches of one `foreach` (`:287-398`), so at most one fires per truck per scan. B is a different module.

### 1.2 Writers that are present in code but **inert** for WW3MOD trucks

These matter because previous fix attempts assumed they were live (§4).

- **`SupplyProvider.SetTarget`'s `MoveTo` (`SupplyProvider.cs:517-524`) never fires outside Hunt stance.** The move is gated on `!InAuraRange(self, target, Info.Range)`. But the normal target search `FindGreatestNeedTarget` (`:366-372`) only enumerates `world.FindActorsInCircle(self.CenterPosition, Info.Range)` and then filters through `IsValidTarget`, which re-checks `InAuraRange` (`:470`). **Every non-Hunt target is in aura range by construction**, so the gate is always false. The only way to get an out-of-range target is `FindNeedsResupplyTarget` (`:299-304`), reachable only at `EngagementStance >= Hunt`. TRUK ships `Defensive` (`AutoTarget.cs:160/163`) and overrides only `InitialResupplyBehavior*` (`vehicles.yaml:514-516`). **This is not a 7-tick order source.** (Contradicts a claim I received during this recon; I verified it directly.)
- **`SupplyProvider.TryRestock` (`:735-741`) is dead for AI trucks.** All call sites (`:233`, `:250`, `:320`) are gated on `ShouldSelfRestock()`, which returns false when resupply behaviour is `Evacuate` (`:332-338`). TRUK is `Evacuate` for the AI (`vehicles.yaml:516`).
- **`DropsSupplyCache`'s `DeliverSupply` handler (`:153-175`)** — no AI path issues that order; human-only.

### 1.3 Incidental grabbers — the ones that select a truck without meaning to

Only one predicate anywhere admits a truck other than SupplyFollower's:

**`GarrisonBotModule` (`:153-162`, `ScanInterval: 200`, `ai.yaml:710`).** `GarrisonActorTypes` is unset in mod YAML, so `IsGarrisonEligible` falls back to `a.Info.HasTraitInfo<PassengerInfo>()` (`:210-218`). **`truk` has `Passenger`**, granted by `^WheeledVehicle` (`vehicles.yaml:116-123`, `CargoType: Vehicle`). So a truck passes, and the module issues `EnterTransport` (`:188`) and `blackboard.ClaimUnit(truck, "garrison")` (`:192`).

- The *move* is a no-op: `Passenger.ResolveOrder` (`Passenger.cs:204`) bails on `IsCorrectCargoType` — garrison buildings take `Types: Infantry`.
- The *claim* is **permanent** — `GarrisonBotModule` never calls `ReleaseUnit`. A truck claimed here is dropped from SupplyFollower's pool by `IsClaimedByOtherModule` (`:627-634`) for the rest of the match.

**This is a separate, live bug: a frozen truck, not an oscillating one.** It is worth fixing but it does not explain the symptom. It is already noted at `WORKSPACE/bugs/discovered.md:6-7`.

Every other module verifiably excludes `truk`: POI offensive/garrison and LaneAmbush by role filter + `ExcludeUnitTypes` (`ai.yaml:366/635/679/1498/1534/1560`); `LayeredDefenceBotModule` by `MainBattle`-only role (`:673`) and `ExcludedActorTypes`; `EngineerRouteOpenBotModule` by a hardcoded `"truk"` exclusion (`:137`); `MountedTransportBotModule` by carrier/passenger whitelists; `SquadManagerBotModule` by `ExcludeFromSquadsTypes` containing `truk` plus `IgnoreGroundUnits: true` — so no `Squad`/`States` order site is reachable; `CaptureCoordinatorBotModule` and `HelicopterSquadBotModule` by `AttackBaseInfo` / `WithInfantryBodyInfo` requirements (a truck is unarmed and not infantry); `ScoutBotModule` by exact name match.

`AutoSeekSupplies` is on `^Soldier` only (`infantry.yaml:221`) — it moves *soldiers toward* trucks, never the truck.

---

## 2. Conditions, cadences, and whether two can be true at once

**Bot tick rate.** `ModularBot` runs every module's `IBotTick` on **every world tick** (`ModularBot.cs:100-125`); there is no bot-level throttle. Each module self-throttles. 25 ticks/sec.

| Source | Cadence | Gate |
|---|---|---|
| SupplyFollower A1–A5 | `ScanInterval: 150` = **~6 s** (`ai.yaml:726`) | truck not claimed elsewhere, not `IsLowOnSupply` |
| DropsSupplyCache B | `OnBecomingIdle` **plus** `ITick` re-check **every tick while idle** (`:200`, `:210-216`) | `supply.CountsAsEmpty` |
| `SupplyProvider.UpdateTarget` (latch only, no orders) | `ScanInterval: 7` ticks (`SupplyProvider.cs:68`) | — |
| DangerFieldLayer rebuild | `UpdateInterval: 25` = **1 s**, round-robin one player per sub-slot (`DangerFieldLayer.cs:192`, `:295-311`) | — |

### 2.1 The actual oscillator (A1 ↔ A2/A4)

**`DangerEvac` is LIVE for every AI profile, not just `@experimental`.** This is the load-bearing finding and it contradicts the code's own comments.

- `ai.yaml:723-748` defines a **single shared** `SupplyFollowerBotModule@supply` with `RequiresCondition: enable-ai-any` and `DangerEvac: true`, `EvacDangerThreshold: 60`, `EvacRetreatCells: 12`, `SectorSpread: true`.
- In code, `evac = Info.DangerEvac && dangerField != null` (`:263`), and `dangerField` is non-null only when `participates` (`:198`).
- `participates = InfluenceStack.Participates(player)` (`:191`), and **`Participates` returns true for `ExperimentalBotType` *and* `StableBotType`** (`InfluenceStack.cs:47-48`).
- `DangerFieldLayer` builds a field for every player `GatherParticipants` returns, which uses the same predicate (`InfluenceStack.cs:56-62`).

Both shipped AI profiles are covered — `Type: experimental` and `Type: stable` (`ai.yaml:32-36`). The comment at `DangerFieldLayer.cs:300-302` ("only @experimental bots + human combatants get a field") and at `SupplyFollowerBotModule.cs:188-190` are **stale**; they describe a narrowing that `Participates` no longer performs.

**The decision function has no hysteresis, no dwell, and no latch:**

```csharp
// SupplyLogisticsMath.cs:111-114
public static bool ShouldEvacuate(int dangerAtTruck, int dangerAtCluster, int threshold)
{
    return dangerAtTruck >= threshold || dangerAtCluster >= threshold;
}
```

Two properties make this pathological rather than merely jittery:

1. **The `dangerAtCluster` term does not respond to the truck moving.** Retreating changes `dangerAtTruck` only. So while the assigned cluster is hot, evac stays true and the truck retreats 12 cells *per scan*, monotonically, until `RetreatTarget` clamps it at the SR (`SupplyLogisticsMath.cs:120-130`).
2. **Selection prefers exactly what evac rejects.** `bestCluster` is `OrderByDescending(c => c.AmmoNeed)` (`:310`; the `SectorSpread` path uses the same need-descending merit via `NeedScore`, `:275`). The neediest cluster is the one that has been shooting, i.e. the one deepest in believed danger.

The forward leg returns when cluster selection switches to a *cooler* cluster. The most reliable source of one is the pool of freshly-called-in units at the SR itself — under the Supply Route model, reinforcements arrive there continuously, so a low-danger, low-`AmmoNeed` cluster reliably exists near the truck once it has retreated. Truck follows it forward; as it advances (or as a hot front cluster re-enters the 35-cell `MaxFollowDistance` window at `:309` and outranks it on `AmmoNeed`), evac fires again.

**Delivery is broken by construction on the retreat leg.** The truck's aura is `Range: 5c0` (`vehicles.yaml:544`); one evac step is 12 cells (`ai.yaml:748`) — 2.4× the aura radius. A truck that evacuates cannot serve anyone, and the resupply push is proximity-only (§3).

This also explains the user's *"not to where units actually need them, but at least closer"*: `FindSafeFollowPosition` (`:549-579`) takes the argmax of `-threat` over a ±3 cell box around the cluster **centroid** — i.e. deliberately the *safest* cell near the cluster, biased away from the enemy and away from the units actually in contact. And `SafeFollowDistance = 5` (`:31`) is **declared and never read anywhere in the file** — the "stay this far behind" knob does nothing.

Compounding it: `AmmoNeed` sums **every** `AmmoPool` on every cluster member (`:523-531`), vehicles included. Per `DOCS/reference/economy.md:28`, a truck can *never* rearm a vehicle. So a pure-armour cluster generates a large `AmmoNeed`, wins the `OrderByDescending`, and attracts a truck that can do nothing for it — and, being armour at the front, reliably triggers evac on arrival.

### 2.2 The secondary loop (A2/A4 ↔ B)

`IsLowOnSupply` (`:640-646`) is `CurrentSupply < RestockThreshold(50) || CountsAsEmpty`. `CountsAsEmpty` is `currentSupply <= 0 || residueUnusable` (`SupplyProvider.cs:153`), and `residueUnusable` is re-latched from a tri-state verdict every **7 ticks** (`:290-295`) and **can flip both ways**. So:

truck's residue reads unusable → SupplyFollower drops it (`:218`, `:238`) → truck idles → `DropsSupplyCache.ITick` fires `RotateToEdge` toward the map edge (`:246`) → a servable soldier wanders into range → verdict flips back → SupplyFollower re-grabs it and cancels the evac (`:353`) → truck idles → residue unusable again.

This is a genuine two-source collision on a 150-vs-7-tick mismatch, and it produces visible thrash. But its rearward leg goes to the **map edge**, not to the base/SR, which does not match the user's description. I rank it **secondary**: real, worth fixing, not the reported symptom. There is also **no hysteresis** on the `50 / 750` threshold.

### 2.3 Arbitration

There are two claim registries and they do not interoperate. `BotBlackboard` unit claims (`:196` / `:214`, enabled `ai.yaml:57`) are read by SupplyFollower, Garrison, Scout and HelicopterSquad. `PoiGoalGuard.Ledger` is read by the POI stack. **`DropsSupplyCache` and `SupplyProvider` consult neither** — they are traits on the actor and act unconditionally. And `BotBlackboard.PostTask` — the *position*-claim half, which already defines `BotTaskType.SupplyRun` (`:24`, `:137`) — has **zero callers engine-wide**.

---

## 3. The resupply delivery model

**Resupply is a proximity PUSH with no driving.** This is the single most important thing to internalise before designing a fix.

- `SupplyProvider.Tick` (`:256-283`) scans `FindActorsInCircle(self.CenterPosition, Info.Range)` (`:372`), picks the greatest-need valid target, and after `RearmDelay` calls `GiveAmmo` directly (`ResupplyTarget`, `:650-700`).
- A unit "becomes known" purely by **being inside the 5-cell aura** at scan time. There is no request, no queue, no demand signal. `AmmoPool.NeedsResupply` exists but has only two readers — the Hunt-stance scan and a production gate (`UnitBuilderBotModule.cs:487`) — neither of which moves a truck.
- **The truck drives toward nothing.** As established in §1.2, `SetTarget`'s `MoveTo` cannot fire outside Hunt stance. All truck movement is external (SupplyFollower / DropsSupplyCache).
- What ends the trip: nothing does, because there is no trip. Delivery ends when the target is full, leaves the aura, or the provider's supply runs out.

**Re §3 of the brief — the `WORKSPACE/DISCOVERIES.md` 2026-08-04 finding is NO LONGER TRUE at this SHA.** The delivery-side range gate now exists: `InAuraRange` (`SupplyProvider.cs:927-930`) is applied at `IsValidTarget` (`:470`), `SetTarget` (`:516`), `SyncTargetCondition` (`:552`) and — the formerly missing one — at delivery in `ResupplyTarget` (`:664-675`), where a decision of "don't deliver" **keeps** the target and re-arms `rearmTicks` rather than dropping it. This is a squared horizontal comparison matching `WorldUtils.FindActorsInCircle` (`WorldUtils.cs:84`) so selection and delivery agree exactly on the boundary. Fixed; `DOCS/reference/economy.md:30` already documents it correctly. **This is not a contributor to the oscillation.**

---

## 4. Why previous fixes did not stick

Every fix so far was made inside whichever module the investigator was reading, and each narrowed *target selection* or added a deadband to *one branch* of *one* writer.

**`48b73c21` (2026-03-20) "Supply truck rework: targeted single-unit resupply"** — made `SupplyProvider` self-driving, adding the `SetTarget` → `MoveTo` at what is now `:522`. As shown in §1.2 this path is unreachable at WW3MOD's default stances, but its *existence* has misled every subsequent fix (see `35804ddc`).

**`3f232c47` / `26480017` (2026-03-23)** — affordability filter and `MinNeedThreshold`. Changed *which* target the provider picks. Cannot address oscillation: the provider never issues movement orders.

**`099394d0` (2026-03-21) "Add SupplyFollowerBotModule"** — created the module that owns all truck movement. Every later oscillation lives here.

**`7be732d5` / `d81039b6` / `753d7c4a` (2026-03-24) stance + `DropsSupplyCache`** — created writer B, hooked to both `OnBecomingIdle` and `ITick`.

**`35804ddc` (2026-05-13) "SupplyFollower stops cancelling its own trucks' restock (O5 fix)"** — the one commit that correctly identified a two-writer collision, and **it fixed the wrong half on a false premise**. It filtered low-supply trucks out of the bot's pool (`IsLowOnSupply`, `:640`) so the bot would stop cancelling `SupplyProvider`'s restock. But `ShouldSelfRestock()` returns false under `Evacuate`, which is TRUK's AI default — **the restock it was protecting does not exist for AI trucks.** The released truck is instead picked up by `DropsSupplyCache` and sent to the map edge. The commit's own comment (still in the tree at `:636-639`, "SupplyProvider's restock … will route it away") is false. It also introduced a bare 50/750 threshold with no hysteresis — creating loop §2.2.

**`ab7bd283` + `057ab755` (2026-07-24) Stage E danger routing + "truck deadband"** — correctly diagnosed a *self*-oscillation (the detour waypoint receded because it was recomputed from the moving truck each scan) and added `lastVia` + `RepathThresholdCells`. **The right idea, applied to one branch only.** It guards A3 and nothing else: A1, A2, A4 and A5 are still re-issued unconditionally every scan. This is the near-miss — the correct fix shape was found and then scoped to the single branch the reviewer happened to be looking at.

**`6fb952c7` (2026-07-24) "unusable residue counts-as-empty and evacuates"** — strengthened writer B and made `residueUnusable` a latch that flips both ways on a 7-tick cadence, which is what makes loop §2.2 fast.

**`be1d1615` (2026-07-30) "@experimental supply-truck logistics — sector spread, small-squad coverage, danger evac"** — **introduced the actual bug.** Added `DangerEvac`/`EvacDangerThreshold`/`EvacRetreatCells` and the stateless `ShouldEvacuate`. Two things went wrong. First, the commit is titled and commented "@experimental", but the flag was set on the **shared `enable-ai-any` instance** and gated on `Participates`, which **includes stable bots** — so it shipped to every AI, which is almost certainly not what was intended. Second, four days later `31409790` (2026-08-03) gave the *infantry* retreat FSM a dedicated `RetreatDamperMath` re-advance dwell for precisely this failure mode; the truck evac never got the equivalent. The correct remedy already exists in the codebase, one module over.

**`5ff82785` + `d2f79fb0` (2026-08-04) Tier-2 idle-truck hunt** — added a fourth destination (A5), re-issued each scan, with no cross-branch arbitration.

**`9ac8f473` / `f15cfbde` (2026-08-04) `AutoSeekSupplies` on by default** — infantry now walk to trucks. Good for the fix direction in §5; neutral here.

**`c97a4ac7`, `57a3d3a5`, `b6012756`, `b75b5703`, `c1028e6b` (2026-08-04)** — rearm-condition lifecycle and aura-range correctness (the §3 fix). None touches movement.

No commit was `git revert`ed. But `35804ddc`'s premise was silently invalidated by the `Evacuate`-default lineage, and `be1d1615`'s "@experimental" scoping was silently invalidated by `Participates` widening to include stable.

**The pattern:** fixes have consistently targeted *which target is chosen* and *one branch's re-issue behaviour*. The failure is at a level above that — **the branch-selection function itself is a memoryless comparator over a 1 Hz-rebuilt field, sampled at 0.17 Hz, whose selection and rejection criteria are positively correlated.** No amount of target-selection tightening reaches it.

---

## 5. The user's proposed direction: designated supply points

> Supply POINTS a bit behind the front line; trucks position there and hold; out-of-ammo units retreat to collect; the truck only goes forward when it must.

**Assessment: this is the right design, it is well-matched to the existing code, and it is buildable without a bot-brain rewrite.**

### 5.1 What already exists

**The pull side is done and shipping.** `AutoSeekSupplies` is a trait on `^Soldier`, `Enabled: true` (`infantry.yaml:221-222`), with no owner split — AI and human infantry behave identically. It fires from `INotifyIdle.TickIdle` (`:91`) every `ScanInterval` 40 with a deterministic per-`ActorID` phase (`:78`) when any pool drops below `AutoSeekAmmoThresholdPerMille` 250 (`:38`), finds the nearest usable provider within `SupplyHuntLeashCells` 20 (`:44`), and runs `SeekSuppliesAndReturn` — a clean out → wait → **return to origin cell** state machine (`SupplyHuntMath.NextState`, `:148`; `:179`). That is already "retreat to the point, collect, go back to the line."

Crucially: it targets an **Actor**, not a position (`SeekSuppliesAndReturn.cs:59-78`) — but that is fine, because a **parked truck is the easy case**. The whole `MaxApproachAttempts = 3` re-plan machinery (`:42`, `:150-155`) exists to cope with a *moving* truck. Parking the truck makes the existing pull side work *better*, not differently. Vehicles are correctly excluded twice (trait is `^Soldier`-only, and `CanServe` requires the `replenish-soldiers` condition, `:207-208`).

**The anchor math exists and is generic.** `ForwardStagingMath.StagingCell` (`ForwardStagingMath.cs:88`) is engine-free integer math: steepest descent down `ControlField`'s distance-to-enemy-frontier field, halting `standoffCells` short of the front, refusing cells over a danger threshold. That is precisely "a stable point a bit behind the front line." It already has Chebyshev hysteresis in `AnchorShifted` (`:168`) — **the dwell that `ShouldEvacuate` lacks**. Despite the name it is not heli-specific; its only consumer is `PoiOffensiveBotModule.ResolveStagingAnchor` (`:1873-1906`), seeded from the player's own SR (`:1101`, `:1881`). `ControlField` is a world-actor trait (`ControlField.cs:380`, registered `world.yaml:375`) reachable by any module via the ordinary `world.WorldActor.TraitOrDefault<>` pattern.

**A position registry is designed but unbuilt.** `BotBlackboard.BotTask` already carries `CPos Location`, `Priority`, `ClaimedBy` and staleness, and even defines `BotTaskType.SupplyRun` (`:24`, `:38-62`, `:137`, `:145`, `:184`). `PostTask` has **zero callers**. This is unwritten code in an existing structure, not a redesign.

### 5.2 What is missing

- **Rally points are unusable.** The SR carries `RallyPoint` (`structures.yaml:272-274`) with no `Path`; the only AI writer picks `possibleRallyPoints.Random(...)` within 8 cells of a building (`BaseBuilderBotModule.cs:227-236`) — random, base-local, front-blind.
- **`ThreatMapManager` is not a front-line derivation.** `GetThreat` (`:197-230`) is a float, **fog-blind** sum over `FindActorsInCircle` — omniscient ground truth, violating the influence-stack integer/zero-RNG invariant (`DOCS/reference/influence-stack.md`). It is what `FindSafeFollowPosition` currently uses, and it is why the follow position is army-relative rather than line-relative. The principled read is `ControlField.FrontierDistanceAt` (`ControlField.cs:893`).
- **Non-influence-stack profiles have no fog-legal front-line source at all.** `ControlField` is narrowed by `Participates`; for `Normal`/`Rush`/`Turtle` bots `StagingCell` would return the SR unchanged and trucks would park at the beachhead. A supply-point rollout is `stable` + `experimental` only unless something else is built.
- **`FrontierStandoffMath` / `EchelonMath` / `LateralSpreadMath` do not help** — they are all target-relative or squad-relative, not map-stable.

### 5.3 Verdict: buildable without a brain rewrite — **yes**

The change is contained to **`SupplyFollowerBotModule.cs`** plus one YAML flag:

1. Resolve `ControlField` in `Initialize()` (`:180-206`), same one-line pattern as `dangerField` (`:198`).
2. Add `ResolveSupplyPoint()` mirroring `PoiOffensiveBotModule.ResolveStagingAnchor` (`:1873-1906`): seed from `FindOwnSupplyRoute()` (already present, `:603-608`), call `ForwardStagingMath.StagingCell` with a standoff larger than the offense module's, and gate re-issue behind `AnchorShifted` hysteresis. No new math file, no new trait.
3. In `BotTick`, on the new flag, replace the per-truck `FindSafeFollowPosition(bestCluster)` target (`:345`) with the anchor cell, optionally one per sector reusing the existing `AssignSectors` spread (`:275-282`) — and **suppress re-issuing the `Move` once the truck is parked**, which is the behavioural core of the whole change.
4. **Turn `DangerEvac` off**, or give `ShouldEvacuate` the same dwell treatment `RetreatDamperMath` gave the infantry FSM in `31409790`. Under a supply-point design the anchor is already behind the line, so the evac branch is largely redundant.
5. Suppress the Tier-2 `IdleTruckHunt` path (`:420-500`) on this flag, or a parked truck will still wander up to 20 cells.

**No new cross-module coordination is required for the core design.** The one thing worth adding — publishing the anchor via `BotBlackboard.PostTask(BotTaskType.SupplyRun, cell, …)` so the offense module and the supply module share one notion of "the line" — is filling in an already-designed, never-called API, and is optional for v1.

Two honest caveats:
- `FindSafeFollowPosition` (`:549-579`) should be **retired** on this path, not extended — its `ThreatMapManager` argmax is the mechanism that makes the position army-relative in the first place.
- **Not statically determinable:** whether infantry that walk to a supply point actually complete the errand. `SeekSuppliesAndReturn` yields to any cancelling order (`:107`), and squad modules re-issue `AttackMove` on their own cadence. Whether the errand survives contact needs a run.

---

## 6. What a live run would be needed to confirm

No test was run for this recon. These conclusions are static and would need one autotest to confirm:

1. **The oscillation period and which loop dominates.** §2.1 (A1↔A2/A4, SR-ward) versus §2.2 (A2/A4↔B, map-edge-ward). The user's description strongly implies §2.1, but only a run showing truck headings vs. `DangerFieldLayer` readings settles it.
2. **How often `dangerAtCluster` actually crosses 60** in a real match, and therefore whether the retreat leg latches for many consecutive scans as §2.1 predicts.
3. **Whether the `GarrisonBotModule` permanent claim (§1.3) is firing in practice** — if it is, some fraction of trucks are frozen rather than oscillating, and the two symptoms would be visually distinct.
4. **Whether infantry complete `AutoSeekSupplies` errands** under squad pressure (§5.3 caveat).

A single `run-test.sh` with truck position logging would separate 1–3. That needs an explicit goahead.

---

## 7. Recommended next step (not performed)

The minimal high-confidence change, in priority order:

1. **Give `ShouldEvacuate` hysteresis + dwell**, modelled on `RetreatDamperMath` (`31409790`) — or set `DangerEvac: false` pending the supply-point work. This alone should stop the reported symptom.
2. **Decide whether `DangerEvac` was ever meant to ship to `stable`.** Given the commit title in `be1d1615`, the `Participates` widening looks accidental.
3. **Drop non-servable pools from `AmmoNeed`** (`:523-531`) — count only pools on actors carrying `replenish-soldiers`, so trucks stop being drawn to armour they cannot serve.
4. **Release the `GarrisonBotModule` claim** (`:192`) or exclude `SupplyProvider` actors from `IsGarrisonEligible` (`:210-218`).
5. Then build supply points per §5.3.

Items 1–4 are independent of the supply-point design and are each small.
