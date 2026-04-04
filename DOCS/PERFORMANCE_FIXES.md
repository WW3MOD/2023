# Performance Fixes — Prioritized by Impact/Effort

Audit date: 2026-04-04

Issues sorted by **highest impact / lowest effort first**.

---

## Tier 1 — Quick Wins (minutes each, noticeable impact)

### 1. Cloak.ShouldHide() — unthrottled full-world scan
**File:** `engine/OpenRA.Mods.Common/Traits/Cloak.cs:321-334`
**Problem:** `ShouldHide()` iterates *every* actor with `DetectCloaked` and computes distance — called every render frame for every cloaked actor, with **no caching**. The caching version is commented out (lines 296-319) with a note about desync.
**Current usage:** Only 2 actors use Cloak (misc.yaml:31, structures-defenses.yaml:787), and DetectCloaked is commented out on all units. So this is **dormant** — but the moment you enable stealth units, it'll be catastrophic.
**Fix:** Uncomment the cached version but only cache per-tick (not per-render), or use a proximity trigger instead of scanning all actors. For now, low actual impact since no DetectCloaked actors are active.
**Impact:** Low now, CRITICAL when stealth is enabled. **Effort:** 5 min.

### 2. GarrisonManager — uncached `self.Trait<BodyOrientation>()` per port per tick
**File:** `engine/OpenRA.Mods.Common/Traits/Garrison/GarrisonManager.cs:408`
**Problem:** `self.Trait<BodyOrientation>()` does a dictionary lookup every tick for every port with a deployed soldier. 4 garrison buildings with 4 ports each = 16 lookups/tick.
**Fix:** Cache `BodyOrientation` in `Created()` or as a field initialized once.
**Impact:** Low-Medium. **Effort:** 2 min.

### 3. GarrisonManager — `TraitsImplementing<AmmoPool>().ToArray()` in reload swap check
**File:** `engine/OpenRA.Mods.Common/Traits/Garrison/GarrisonManager.cs:448`
**Problem:** Allocates a new array every scan interval for every deployed soldier with no target. This is inside the idle-recall path, called every `TargetScanInterval` ticks per port.
**Fix:** Cache AmmoPool arrays on the deployed soldier when deploying, or use a simple foreach instead of `.ToArray()`.
**Impact:** Low. **Effort:** 5 min.

### 4. Armament.FireBarrel() — `.ToArray()` on every shot
**File:** `engine/OpenRA.Mods.Common/Traits/Armament.cs:403-405`
**Problem:** `damageModifiers.ToArray()`, `inaccuracyModifiers.ToArray()`, `rangeModifiers.ToArray()` allocate 3 new arrays every single time a weapon fires. With 50 units firing, that's 150 array allocations per volley.
**Fix:** Cache modifier arrays and invalidate on condition changes, or use stackalloc/pooled arrays.
**Impact:** Medium during combat. **Effort:** 15 min.

### 5. Armament.UpdateMagazine() — `.ToArray()` on reload
**File:** `engine/OpenRA.Mods.Common/Traits/Armament.cs:474`
**Problem:** `reloadModifiers.ToArray()` allocates every magazine reload.
**Fix:** Same as above — cache and invalidate.
**Impact:** Low-Medium. **Effort:** 5 min (do with #4).

---

## Tier 2 — Moderate Effort, Significant Impact

### 6. AutoTarget.ChooseTarget() — `.ToList()` on every target scan
**File:** `engine/OpenRA.Mods.Common/Traits/AutoTarget.cs:637`
**Problem:** `activeTargetPriorities.ToList()` allocates a new list every time any unit scans for targets (every 10-40 ticks per unit). With 100 units this is 2-10 list allocations per tick.
**Fix:** Cache the filtered list and invalidate when traits are enabled/disabled. Or iterate the IEnumerable directly without materializing.
**Impact:** Medium. **Effort:** 15 min.

### 7. HealerAutoTarget — double FindActorsInCircle per scan
**File:** `engine/OpenRA.Mods.Common/Traits/HealerAutoTarget.cs:132,190`
**Problem:** `FindBestTarget()` and `FindCriticalUnclaimed()` each do a full spatial query. That's 2 spatial scans every 8 ticks per medic/engineer.
**Fix:** Share the spatial query result between both methods. One `FindActorsInCircle` call, iterate twice.
**Impact:** Medium with multiple medics. **Effort:** 15 min.

### 8. Bot AI Squad States — LINQ chains in every bot tick
**Files:**
- `engine/OpenRA.Mods.Common/Traits/BotModules/Squads/States/GroundStates.cs:49`
- `NavyStates.cs:73`
- `AirStates.cs:177,209`
- `HelicopterStates.cs:197,265`

**Problem:** `.Where().ToList()` and `.ToArray()` in bot squad tick methods. Each bot squad allocates filtered lists every tick.
**Fix:** Use foreach with if-checks instead of LINQ, or cache results for N ticks.
**Impact:** Medium with bots. **Effort:** 30 min (multiple files).

### 9. SpreadsCondition — FindActorsInCircle + TraitOrDefault every delay ticks
**File:** `engine/OpenRA.Mods.Common/Traits/Conditions/SpreadsCondition.cs:62-63`
**Problem:** Spatial query + `TraitOrDefault<SpreadsCondition>()` per found actor, every `Delay` ticks. `TraitOrDefault` is a dictionary lookup per actor.
**Current usage:** Not used in WW3MOD YAML (0 matches). **Dormant.**
**Fix:** Not needed unless enabled. If enabled, filter by trait existence via `ActorsWithTrait<>` instead.
**Impact:** Zero (unused). **Effort:** 5 min if ever needed.

---

## Tier 3 — Larger Refactors, High Impact

### 10. Modifier collection pattern (engine-wide)
**Problem:** The pattern `someModifiers.ToArray()` → `Util.ApplyPercentageModifiers()` appears throughout the engine. Every modifier application allocates. This affects Armament, Speed, Damage, Range — anything that uses the modifier stack.
**Fix:** Create a shared `Util.ApplyPercentageModifiers(IEnumerable<int>)` overload that iterates without allocating, or use a pooled array buffer.
**Impact:** High (affects all units every tick). **Effort:** 1-2 hours.

### 11. AttackBase.ScanForNewTarget() — `.ToList()` allocation
**File:** `engine/OpenRA.Mods.Common/Traits/Attack/AttackBase.cs:215`
**Problem:** Target scanning materializes LINQ query results. Every unit with AttackBase allocates during scans.
**Fix:** Iterate directly or use a pooled list.
**Impact:** Medium-High. **Effort:** 20 min.

### 12. ActorMap proximity triggers — LINQ in CellTrigger/ProximityTrigger tick
**File:** `engine/OpenRA.Game/Map/ActorMap.cs:76,141-144`
**Problem:** `UnionWith(Footprint.SelectMany(...))` and `.Where()` chains create enumerator objects every tick for active triggers. Every ProximityExternalCondition, SupplyRouteContestation, etc. that uses proximity triggers contributes.
**Fix:** Pre-allocate trigger working sets, reuse HashSets instead of creating enumerator chains.
**Impact:** Scales with number of proximity-based traits. **Effort:** 1 hour.

---

## Tier 4 — Architectural / Long-term

### 13. GarrisonManager spatial queries per port
**File:** `engine/OpenRA.Mods.Common/Traits/Garrison/GarrisonManager.cs:468`
**Problem:** `ScanForTarget()` calls `FindActorsInCircle()` for each empty port on its scan interval. 4 buildings x 4 ports = up to 16 spatial queries per scan cycle.
**Fix:** Share one spatial query per building (scan once, distribute targets to ports). Already staggered across ticks which helps, but the underlying query is still per-port.
**Impact:** Medium with many garrison buildings. **Effort:** 30 min.

### 14. Lambda captures in Armament.FireBarrel()
**File:** `engine/OpenRA.Mods.Common/Traits/Armament.cs:416`
**Problem:** `ScheduleDelayedAction` captures `delayedTarget`, `args`, `barrel`, etc. in a closure. Each shot allocates a closure object + delegate.
**Fix:** Use a struct-based delayed action queue instead of lambdas, or pool the closures. This is a deep refactor.
**Impact:** Medium during heavy combat. **Effort:** 2+ hours.

### 15. ConquestVictoryConditions — LINQ chains in Tick
**File:** `engine/OpenRA.Mods.Common/Traits/Player/ConquestVictoryConditions.cs:84-88`
**Problem:** `.Where().GroupBy().All()` chain runs every tick to check victory state.
**Fix:** Only check every N ticks (victory conditions don't need per-tick precision), or cache player lists.
**Impact:** Low (runs once per player, not per unit). **Effort:** 10 min.

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

---

## Summary

**Biggest bang for buck:** Items 4-6 (Armament + AutoTarget `.ToArray()`/`.ToList()` allocations). These affect every unit in combat every few ticks. Fixing them is straightforward — cache the arrays and invalidate on condition change.

**Biggest systemic fix:** Item 10 (modifier collection pattern). A single utility change that eliminates allocations across the entire modifier stack.

**Most units affected by:** Items 4, 5, 6, 10, 11 — the combat hot path (weapons firing, target scanning, modifier application).

**Safe to ignore for now:** Items 1, 9 (dormant traits not used in YAML).
