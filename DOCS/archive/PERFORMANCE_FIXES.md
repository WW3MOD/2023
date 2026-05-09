# Performance Fixes — Prioritized by Impact/Effort

Audit date: 2026-04-04
**Applied:** 2026-04-04 (items marked [DONE])

Issues sorted by **highest impact / lowest effort first**.

---

## Tier 1 — Quick Wins (minutes each, noticeable impact)

### 1. Cloak.ShouldHide() — unthrottled full-world scan
**File:** `engine/OpenRA.Mods.Common/Traits/Cloak.cs:321-334`
**Problem:** `ShouldHide()` iterates *every* actor with `DetectCloaked` and computes distance — called every render frame for every cloaked actor, with **no caching**. The caching version is commented out (lines 296-319) with a note about desync.
**Current usage:** Only 2 actors use Cloak (misc.yaml:31, structures-defenses.yaml:787), and DetectCloaked is commented out on all units. So this is **dormant** — but the moment you enable stealth units, it'll be catastrophic.
**Fix:** Uncomment the cached version but only cache per-tick (not per-render), or use a proximity trigger instead of scanning all actors. For now, low actual impact since no DetectCloaked actors are active.
**Impact:** Low now, CRITICAL when stealth is enabled. **Effort:** 5 min.

### 2. [DONE] GarrisonManager — uncached `self.Trait<BodyOrientation>()` per port per tick
**File:** `engine/OpenRA.Mods.Common/Traits/Garrison/GarrisonManager.cs`
**Fix applied:** Cached BodyOrientation in Created(). Also cached Armament[] on PortState (set on deploy, cleared on recall) to eliminate TraitsImplementing.ToArray() in ScanForTarget.

### 3. [DONE] GarrisonManager — `TraitsImplementing<AmmoPool>().ToArray()` in reload swap check
**File:** `engine/OpenRA.Mods.Common/Traits/Garrison/GarrisonManager.cs`
**Fix applied:** Replaced `.ToArray().All()` with zero-allocation foreach loop.

### 4. [DONE] Armament — redundant `.ToArray()` on modifier IEnumerables
**File:** `engine/OpenRA.Mods.Common/Traits/Armament.cs`
**Fix applied:** Removed `.ToArray()` from MaxRange(), UpdateMagazine(), ResetBurst(), and burst wait — `ApplyPercentageModifiers` already accepts `IEnumerable<int>`. Note: FireBarrel's `.ToArray()` is needed because ProjectileArgs stores `int[]` (values must be captured at fire time).

### 5. [DONE] (Merged with #4)

---

## Tier 2 — Moderate Effort, Significant Impact

### 6. [DONE] AutoTarget.ChooseTarget() — `.ToList()` on every target scan
**File:** `engine/OpenRA.Mods.Common/Traits/AutoTarget.cs`
**Fix applied:** Replaced with reusable `List<>` fields (`reusableActivePriorities`, `reusableValidPriorities`). Also converted `HasValidTargetPriority` from `.Any()` LINQ to foreach loop. Cached trait array `allTargetPriorities` with runtime IsTraitDisabled filtering.

### 7. [DONE] HealerAutoTarget — duplicate AttackBase iteration per scan
**File:** `engine/OpenRA.Mods.Common/Traits/HealerAutoTarget.cs`
**Fix applied:** Cached AttackBase[] in Created() via INotifyCreated. Extracted shared GetMaxHealRange() helper.

### 8. [DONE] Bot AI Squad States — LINQ chains in every bot tick
**Files:** GroundStates.cs, NavyStates.cs, AirStates.cs, HelicopterStates.cs
**Fix applied:** Replaced `.Where().ToList()` and `.ToArray()` with foreach+if loops.

### 9. SpreadsCondition — FindActorsInCircle + TraitOrDefault every delay ticks
**File:** `engine/OpenRA.Mods.Common/Traits/Conditions/SpreadsCondition.cs:62-63`
**Problem:** Spatial query + `TraitOrDefault<SpreadsCondition>()` per found actor, every `Delay` ticks. `TraitOrDefault` is a dictionary lookup per actor.
**Current usage:** Not used in WW3MOD YAML (0 matches). **Dormant.**
**Fix:** Not needed unless enabled. If enabled, filter by trait existence via `ActorsWithTrait<>` instead.
**Impact:** Zero (unused). **Effort:** 5 min if ever needed.

---

## Tier 3 — Larger Refactors, High Impact

### 10. [DONE] Modifier collection pattern — Turn.cs + Turreted.cs
**Fix applied:** Removed redundant `.ToArray()` in `Turn.cs` (turn speed) and `Turreted.cs` (turret turn speed) where `ApplyPercentageModifiers` already accepts `IEnumerable<int>`. Other call sites (warheads, AreaBeam) need `.ToArray()` because they pass to `ProjectileArgs.DamageModifiers` which is `int[]`.

### 11. [DONE] AttackBase.ScanForNewTarget() — `.ToList()` + `.OrderBy()` allocation
**File:** `engine/OpenRA.Mods.Common/Traits/Attack/AttackBase.cs`
**Fix applied:** Replaced `.ToList().OrderBy().First()` with single-pass closest-target scan.

### 12. ActorMap proximity triggers — LINQ in CellTrigger/ProximityTrigger tick
**File:** `engine/OpenRA.Game/Map/ActorMap.cs:76,141-144`
**Problem:** `UnionWith(Footprint.SelectMany(...))` and `.Where()` chains create enumerator objects every tick for active triggers. Every ProximityExternalCondition, SupplyRouteContestation, etc. that uses proximity triggers contributes.
**Fix:** Pre-allocate trigger working sets, reuse HashSets instead of creating enumerator chains.
**Impact:** Scales with number of proximity-based traits. **Effort:** 1 hour.

---

## Tier 4 — Architectural / Long-term

### 13. GarrisonManager spatial queries per port
**File:** `engine/OpenRA.Mods.Common/Traits/Garrison/GarrisonManager.cs`
**Problem:** `ScanForTarget()` calls `FindActorsInCircle()` for each empty port on its scan interval. Different ports may have different ranges (different soldiers), making sharing complex. Already staggered across ticks.
**Impact:** Medium with many garrison buildings. **Effort:** 30 min.

### 14. Lambda captures in Armament.FireBarrel()
**File:** `engine/OpenRA.Mods.Common/Traits/Armament.cs:416`
**Problem:** `ScheduleDelayedAction` captures `delayedTarget`, `args`, `barrel`, etc. in a closure. Each shot allocates a closure object + delegate.
**Fix:** Use a struct-based delayed action queue instead of lambdas, or pool the closures. This is a deep refactor.
**Impact:** Medium during heavy combat. **Effort:** 2+ hours.

### 15. ConquestVictoryConditions — LINQ chains in Tick
**File:** `engine/OpenRA.Mods.Common/Traits/Player/ConquestVictoryConditions.cs:84-88`
**Problem:** `.Where().GroupBy().All()` chain — but only in `NotifyTimerExpired` (runs once at game end), NOT in Tick. The Tick method already has cached `otherPlayers` with `??=` and uses foreach. **Not actually a problem.**

---

## Not Actually Problems (False Positives)

| Item | Why it's fine |
|------|--------------|
| Activity.cs Console.WriteLine (line 248) | Only in `PrintActivityTree()` — a debug method, not called per-tick |
| Server TraitInterfaces.cs Console.WriteLines | Server lifecycle events only, not per-tick |
| PlaceBuilding.cs LINQ (line 247) | Only runs when `triggerNotification` is true (rare) |
| ProximityExternalCondition.cs position update | Already has early-out when position unchanged (line 102) |
| SmartMove.cs | Only checks damage state in callback, no per-tick scanning |
| CohesionMoveModifier.cs | Runs at order-issue time, not per-tick |
| SupplyRouteContestation.cs | Uses proximity triggers (event-driven), recalc every 7 ticks — already well-optimized |
| ConquestVictoryConditions.cs Tick | Already uses `??=` cache and foreach — the LINQ is only in timer-expired path |

---

## Bonus fixes (found and applied during audit)

### [DONE] Multiplayer sync risks — float math and missing [Sync] attributes
- HeliAutorotate: replaced float division with integer math (float can differ across CPUs → desync)
- CargoSupply: converted CalculateNeed from float to integer millipercent
- CargoSupply: added [Sync] to supplyCount and effectiveSupply
- SupplyRouteContestation: added [Sync] to controlBar, defeatBar, isPassive

---

## Summary

**Applied 10 of 15 items.** Remaining 5 are either dormant (Cloak, SpreadsCondition), architectural (ActorMap, lambda closures), or not actually problems (ConquestVictoryConditions).

**Biggest wins:** AutoTarget reusable lists (#6), Armament modifier cleanup (#4), Bot AI LINQ removal (#8), AttackBase single-pass scan (#11), Turn/Turreted modifier cleanup (#10).

**Remaining high-value targets:** #12 (ActorMap proximity triggers) and #14 (lambda closures in Armament) would require deeper refactoring.
