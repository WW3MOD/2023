# Recon — can MapShadowLayer be updated live, in deferred batches? (2026-09-08)

Read at **`main @ f83826d4`** (worktree `wt/shadow-relight`, branched from it; `main` was
4 ahead of `origin/main`, 0 behind, at the time of reading). Harness committed at
`9e380427` on this branch. Build: `.\make.ps1 all`, 0 warnings 0 errors. No game launched,
no `--check-yaml`, no `make test`.

Measurements are on **woodland-warfare-ww3 (98x98, 9604 cells)** and
**river-zeta-ww3 (98x82, 8036 cells)**, on a **16-core** box. Reproduce with
`--measure-shadow` / `--measure-shadow-crater` (see §Harness).

---

## VERDICT

**Affordable at the unit of one burnt tree. NOT affordable at the unit of a nuclear
crater — and the crater is the case the burn-in-place design exists for.**

The decisive finding is not the incremental cost. It is that **a full parallel rebuild of
the entire layer costs 0.8–1.3 s, which is CHEAPER than incrementally updating the pairs a
single Tsar Bomba crater invalidates.** Every incremental scheme is more complex, more
sync-fragile, and slower than simply rebuilding everything off-thread and swapping at a
sim-determined tick. That inverts the framing the question was asked in, and neither prior
recon nor `DISCOVERIES.md` considered it.

**Build: option 4 (off-thread full rebuild, deterministic swap deadline) if the gap is worth
closing at all. Build option 1 (nothing) if it is not — and there is a standing owner ruling
that says it is not.**

---

## 1. The unit of recomputation — CONFIRMED

`Map.RecomputeShadowFrom(MPos fromUV)` (`Map.cs:1195`) is **per-from-cell, computing that
cell's whole annulus**. It iterates `FindTilesInAnnulus(from, 2, 32)` and, for each to-cell,
walks `TilesIntersectingLine` accumulating two sums, then writes one `(byte, byte)` pair.

| | woodland-warfare | river-zeta |
|---|---|---|
| to-cells per from-cell (mean / max) | 2369.7 / 3204 | 2293.7 / 3204 |
| total stored pairs | 22,758,964 | 18,431,956 |
| **one `RecomputeShadowFrom` call** | **1903.7 µs** | **1838.3 µs** |
| derived **cost per (from,to) pair** | **0.803 µs** | **0.801 µs** |
| full serial bake | 13,132 ms | 11,254 ms |
| **full parallel bake (16 cores)** | **1300 ms** | **821 ms** |

A tick is **60 ms** (`mod.yaml:379-403`, `DefaultSpeed: default` → `Timestep: 60`, i.e.
**16.67 tps**). One `RecomputeShadowFrom` call is 3.2% of a tick.

river-zeta's 18,431,956 pairs reconcile exactly with the shipped cache file: payload
36,871,948 B = 8036 density bytes + 2 × 18,431,956. `DISCOVERIES.md`'s 18,435,974 divided
the whole payload by two without subtracting the density layer; the byte figure is right.

### A false claim in the existing dead code

`ShadowUpdateBudgetPerTick = 1000` (`Map.cs:270`) carries the comment *"1000/tick × 3200 =
3.2M ops/tick — well within budget at 25 tps. Single building (~3200 from-cells) converges
in ~3 ticks (0.12s)."* **Both halves are wrong.** 1000 from-cells/tick is
1000 × 1.9 ms = **1.9 seconds per tick**, i.e. **32× the entire tick budget**, not "well
within" it. And "25 tps" is the same **1.5× tick-rate error** `CLAUDE.md` flags at ten other
sites — the real rate is 16.67 tps, so the quoted 0.12 s is really 0.18 s even on its own
(wrong) cost model. Anyone reviving this path from its comment would budget ~30× too
optimistically.

## 2. Blast radius of one changed cell — CONFIRMED, and it is separable

**Yes, the computation decomposes per-ray, and the win for a single cell is large.**

For one changed cell X (woodland, forest interior, density 15):

| | from-cells | pairs |
|---|---|---|
| **naive** — what `UpdateShadowForCells` does | 3209 | 9,438,620 |
| **exact** — rays actually crossing X | 2984 | **58,300** |

**Waste factor 161.9× (exact is 0.62% of naive).** river-zeta independently: **163.3×**. The
ratio is stable across maps. Note the from-cell counts barely differ (2984 vs 3209) — almost
every from-cell in radius 32 has *some* ray through X, just ~19.5 of its ~2370 rays (max 485,
for from-cells adjacent to X). **The from-cell is the wrong unit; the ray is the right one.**

### The accumulation is separable AND subtractable — CONFIRMED byte-exact

`totalGroundDensity` is a plain sum over the intermediate tiles and `ForestGroundShadow` is
applied once at the end (`Map.cs:1215-1252`), so a change of δ at X shifts the sum by exactly
δ for every crossing ray. The airborne term is a float sum, gated per-tile on
`ShadowObstacleHeight > z_los`.

Harness result over **all 58,300 crossing pairs**, comparing a full re-walk against patching
the original raw accumulators by the delta:

```
ground   rebuild-vs-subtract mismatches: 0
airborne rebuild-vs-subtract mismatches: 0   (including raw float bit-equality, not just the ceil)
```

The airborne subtraction is exact for a non-obvious reason worth recording: every authored
density in the mod is a **multiple of 5** (`0, 5, 10, 15, 20, 50`; max stamped value 35; zero
stamped cells that are not a multiple of 5), so `density / 5f` is always an exact small
integer in float and the sum never drifts. **This is the same fragile invariant already
documented in-tree for `ForestGroundShadow`'s below-knee path** (`Map.cs:1176-1181`). A future
density of 7 would break both at once.

> **Design constraint that follows:** whatever density a *burnt* tree is given, it **must stay
> a multiple of 5** (15 → 5 or 0, not 15 → 7).

The subtraction must re-evaluate X's own `z_los` gate for that pair — O(1) arithmetic, no ray
walk. Subtracting unconditionally is wrong for ~77% of crossing pairs; that was a bug in my
first harness pass, not a property of the algorithm.

## 3. The crux fails at crater scale — CONFIRMED

The 162× win is a property of a **single** changed cell. It collapses as the changed region
grows, because a contiguous blob is crossed by a much larger share of all rays
(woodland, crater centred in the forest):

| crater | cells | naive pairs | **exact wrong pairs** | waste factor |
|---|---|---|---|---|
| r=0 (one tree) | 1 | 9,438,620 | **58,300** | **161.9×** |
| r=2 (`Atomic`-scale) | 13 | 10,398,670 | **317,100** | 32.8× |
| r=8 | 197 | 13,676,678 | **1,639,042** | 8.3× |
| **r=15 (`NukeTsarBomba`-scale)** | **709** | 16,952,310 | **4,126,526** | **4.1×** |

Cost of each strategy for the r=15 crater, at the measured 0.803 µs/pair and a 60 ms tick:

| strategy | work | wall-clock | ticks |
|---|---|---|---|
| naive from-cell recompute (`UpdateShadowForCells`) | 6393 calls | **12.2 s** | 203 |
| per-ray **re-walk** of only the wrong pairs | 4.13 M pairs | **3.3 s** | 55 |
| per-ray **subtract** (needs stored raw sums) | 4.13 M pairs | ~21 ms | <1 |
| **finding** the wrong set by brute force | — | **12.1 s** | 201 |
| **full parallel rebuild of the whole layer** | — | **1.3 s** | 22 |

Two things jump out.

**Discovery dominates.** Brute-force discovery (12.1 s) costs more than the naive recompute it
was meant to replace. A precomputed "which pairs cross cell C" index is not an option either:
22.76 M pairs × ~30 crossed cells ≈ 683 M entries ≈ **2.7 GB**. Discovery has to be a cheap
geometric predicate (segment-vs-disc for a crater), which is plausible but **unmeasured**.

**The full rebuild beats every incremental path at crater scale.** 1.3 s, exact, using code
that already ships and is already proven deterministic.

### The `~90%` claim from DISCOVERIES.md — HOLDS, with a caveat

`DISCOVERIES.md` (2026-09-08) says a Tsar Bomba "re-runs ~90% of the bake ... inside a single
simulation tick". Its arithmetic is sound: crater radius ~15 + annulus radius 32 → a radius-47
affected disc = π·47² ≈ 6940 cells against river-zeta's 8036, ≈86%, and it was measuring the
*naive from-cell set*, serially, via `UpdateShadowForCells`. I measure 6393/9604 = **66.6%** on
woodland — the difference is map size and detonation position (a centre hit on the smaller
river-zeta clips less). **The magnitude claim holds: 12.2 s of serial work in one 60 ms tick.**
Do not read the exact percentage as map-independent; it is 30% for a corner detonation.

## 4. What happens today — CONFIRMED: nothing, and the machinery is already written

**Nothing mutates either layer at runtime.** The exhaustive caller grep returns only commented
-out lines:

- `Map.UpdateShadowForCells` (`Map.cs:1080`), `QueueShadowUpdate` (`:1099`),
  `FlushPendingShadowUpdates` (`:1109`), `UpdateDensityForBuilding` (`:1263`) — all flagged
  `CURRENTLY UNUSED (260503)`.
- `World.cs:517` — the flush call, commented out.
- `Building.cs:381-382, 395-396` — the enqueue calls, commented out.

**So the deferred, budgeted queue the question proposes already exists in full** — dirty set,
expansion to from-cells, per-tick budget, tick call site. It was disabled 260503 for *"visible
mid-game lag when many buildings changed at once"*, and §1 shows why: its budget constant is
~30× more expensive than its own comment claims.

**The current tree-removal path does nothing to the shadow layer, because there is no tree
removal.** Trees are `^TreeIndestructible` (`DamageMultiplier` 0, 2026-09-02 owner ruling,
`decoration.yaml:65-70`) — they never lose HP, never die, never spawn their husks. The same
comment block carries an explicit standing ruling:

> **`DEFERRED, DO NOT BUILD: recomputing forest shadow when trees die (owner: "it gets
> complicated and it is not the right time now to invest time into"). Treating living and
> burnt identically is what makes that unnecessary for now.`**

Burn-in-place is not a workaround someone invented to dodge the bake; it is the shipped design,
and the shadow gap is a known, owner-accepted deferral.

### Both prior recon docs are stale on mechanism (verdict still right)

`260728-shadowlayer-tree-death.md` and `260802-shadowlayer-tree-death.md` describe
`ShadowLayer` as `CellLayer<CellLayer<(byte,byte)>>` and the load path as reading a
`shadows.bin` **inside the map package**. Both are now false: the layer is the flat
`MapShadowLayer`, and an in-package `shadows.bin` is *deliberately not consulted*
(`Map.cs:495-508`). Their BAKED verdict is unchanged and still correct.

Their headline risk is also now **cured**. Both warn that "the real risk is baseline
divergence — one client loads a stale bin while another regenerated". `ShadowCache` closes
exactly that: the key is `SHA1(mapUid | densityRulesHash | AlgoVersion)`
(`ShadowCache.cs:78-92`), every term content-derived, and an unvalidatable in-package file is
refused. Do not carry that warning forward.

## 5. Determinism — the constraint does NOT kill the idea

**The parallel bake's determinism mechanism is reusable, and it is not about ordering.**
`SetShadowLayer` (`Map.cs:1055-1070`) is safe under `Parallel.ForEach` because **each
from-cell owns a disjoint slot range** in the flat array and no accumulation crosses a
from-cell boundary — so scheduling cannot change the result at all. `MapShadowLayerParallelismTest`
attacks that claim at the store (it fills serially and concurrently and compares every pair);
it does **not** call `RecomputeShadowFrom`.

That argument transfers verbatim to an incremental design **provided the unit of parallel work
stays a whole from-cell**. Even a per-ray scheme keeps it if the wrong pairs are grouped by
from-cell before dispatch. This is the reusable mechanism the question asked about: **yes, and
the grouping is the condition.**

Point by point:

- **`ShadowCache` does not assume the layer is immutable after load.** `TrySave` is reachable
  only from `RegenerateAndCacheShadows`, only in the cache-miss branch of
  `LoadOrGenerateShadows`, during `Map` construction, before a `World` exists
  (`Map.cs:504-526`). A runtime mutation can never be written back. Nothing breaks.
- **Replays are fine.** The key is fully content-derived, so every client and every replay
  regenerates the identical *baseline*; mutations then arrive through the ordered sim. The
  `AlgoVersion` comment's replay concern is about the baseline and is untouched.
- **The existing dirty set is deterministic by accident, not by construction.**
  `pendingShadowDirtyCells` is a `HashSet<CPos>` iterated with `foreach` (`Map.cs:260, 1117`).
  It happens to be reproducible across clients because insertion sequences and `CPos` hash
  codes match — but BCL enumeration order is not contractual. **A real incremental design must
  sort the dirty set explicitly.** This is a live defect in the dead code, not a hypothetical.
- **The budget is safe.** `ShadowUpdateBudgetPerTick` is a compile-time const, and the flush
  site sits inside `if (SimulationIsAdvancing)` next to `WorldTick++` (`World.cs:500-517`) —
  no wall-clock, no frame rate, no `Game.LocalTick`. The scaffolding got this right.
- **Order-independence is only partial, and this is the subtle one.** Because each from-cell's
  recompute is independent and idempotent, drain order does not affect the *converged* layer.
  It absolutely does affect the *intermediate* layer, and the sim reads the intermediate layer
  every tick of the convergence window. **The several seconds of staleness the question offers
  to tolerate is exactly the window in which order determinism is load-bearing.** Tolerating
  staleness does not buy tolerance of ordering.
- **Float exposure is not new but the subtraction shape is.** Measured bit-exact (§2), resting
  on the multiple-of-5 invariant.

## 6. How much shadow is actually at stake — CONFIRMED, and it is material

Removing one cell's density and diffing every affected pair:

| tree | density | ground shadow delta | airborne delta | pairs shifted |
|---|---|---|---|---|
| T15 (woodland) | 15 | **−3** on 57,774 of 58,300 | **−3** on 13,576 | 58,300 |
| density-5 cell (river-zeta) | 5 | **−1** on all 15,985 | −1 on 5,169 | 16,043 |

Above the knee the curve is `2 + ceil((d−20)/5)`, so a tree is worth **density/5** ground
shadow units: a standard density-10 tree = **2**, a T15 = **3**. Airborne is `density/5`
exactly, same magnitude.

Against the vision ladder quoted at `Map.cs:1168-1169` (strength 10 @0-4c, 9 @4-7c, 8 @7-10c,
7 @10-13c, 6 @13-16c) and `Detection hides Vision-3 when (strength − shadow) <= 3`: at 13–16
cells a viewer has strength 6, so **a single T15 on the sightline is the whole difference
between concealed and seen**. Only ~23% of crossing pairs are affected on the airborne term,
because that term counts only tiles in the last quarter of the ray (`z_los` gate → `t > 0.75`).

**This is not a marginal correctness gain, so §5's "the delta is too small to bother" escape
route is not available.** But note the delta above is for a tree *removed*. A tree *burnt*
should keep its trunk, so the honest delta is whatever fraction of 10-or-15 the trunk is not —
and it must land on a multiple of 5.

---

## Ranked options

### 1. Do nothing: a burnt trunk blocks LOS at full strength. **Zero cost.**
Defensible for the **ground** term — a charred trunk really does still stop a rifle sightline,
and `ForestGroundShadow`'s input is a physical occlusion sum, not a foliage count. Much weaker
for the **airborne** term, which is the canopy term by construction (it only counts obstacles
in the last quarter of the ray, i.e. near the target) — a burnt canopy genuinely should stop
hiding you from a helicopter. This is the shipped state and has a standing owner ruling behind
it. **Cost 0, correctness gap confined to the airborne term.**

### 2. Off-thread full rebuild, swapped at a sim-determined tick. **~1.3 s of background CPU.** ← *what I would build*
On a qualifying event (a nuke, or a batch of burns crossing a threshold), snapshot
`DensityLayer`, build a **whole new `MapShadowLayer`** on background threads with the existing
`Parallel.ForEach` bake, and swap the reference at tick **T+K** where T is the sim tick of the
event and K is a fixed const. Every client swaps at the same tick regardless of when its own
threads finished; a client that is not done blocks (correctness preserved, hitch risk).

- **Exact**, not approximate. No stale pairs anywhere afterwards.
- **Reuses the shipped, already-proven-deterministic bake.** No new algorithm, no per-ray
  index, no stored raw sums, no new float shape, no new determinism argument beyond "the swap
  tick is sim-derived".
- **Cheaper than the incremental path it replaces** at crater scale: 1.3 s versus 3.3 s of
  re-walk plus a discovery problem I could not cost.
- Staleness is a fixed, sim-determined K ticks — precisely the "few seconds" on offer.
- **Costs 61.6 MB transient** for the second layer during the rebuild (one large array, a much
  better GC profile than the 8,036-small-arrays shape `MapShadowLayer`'s comment describes
  replacing).
- **The risk is core count** — see "what to measure next".

### 3. Per-ray incremental, deferred and budgeted. **47 ms per tree; 3.3 s + uncosted discovery per crater.**
The 162× win is real and I confirmed subtraction is byte-exact, so this is technically sound.
But its cost profile is **inverted against the need**: cheapest for a single burning tree
(which nobody will notice) and worst for a crater (the case the design exists for). It also
needs either a 2.7 GB index or an unproven geometric predicate, and — if you want the O(1)
subtract rather than the 0.80 µs re-walk — stored raw sums at **3× the layer memory**
(61.6 → 185 MB), reintroducing exactly the pressure `MapShadowLayer` was written to remove.
Build this only if the requirement is genuinely "individual trees burn one at a time".

### 4. Revive the existing deferred queue as written. **12.2 s of CPU per crater.**
`FlushPendingShadowUpdates` already exists and its call site is one uncommented line. But at
the naive from-cell unit that is 12.2 s spread at whatever budget you pick: at a defensible
6 ms/tick it is **~2000 ticks ≈ 2 minutes** of staleness, an order of magnitude past the
tolerance offered. Its own budget comment is wrong by ~30× (§1). **Reject.**

### 5. Change only the *rendered* shadow, leave the vision layer alone. **Impossible here.**
**There is no rendered shadow.** Every reader of `ShadowLayer` is simulation:
`MapLayers.cs` (vision attenuation), `FiringLOS.cs`, `AutoTarget.cs`, `AttackBase.cs`,
`WeaponInfo.cs` (`MaxShadowToFire`), plus `TestGlobal.cs`. No renderer touches it. The word
"shadow" here means LOS attenuation, not a visual. The two are not separable because only one
of them exists.

### 6. Precompute both states. **Refuted, as suspected.**
Any subset of burnt trees is a different layer, and a crater is always a partial burn — an
all-trees and a no-trees layer bracket the answer but are never *the* answer. It would also
cost 2 × 61.6 MB to get two states that are both wrong. Refuted in one line; not explored
further.

---

## What to measure next, to be sure of option 2

1. **The parallel bake on a low-core client.** My 1300 ms is on **16 cores** at a 10.1×
   speedup over serial. Scaling by cores puts a 4-core client near **3.3–3.7 s** and a 2-core
   client near **6.6–7 s** — a 2-core machine would miss a 6 s deadline and stall. This single
   number decides whether option 2 is safe, and it is the one I could not get from this box.
   *Answer that counts:* wall-clock of `SetShadowLayer` on woodland-warfare with
   `Parallel.ForEach` capped to `MaxDegreeOfParallelism` 2 and 4.
2. **Whether `DensityLayer` needs snapshotting** during the background rebuild, i.e. whether
   any further burn can land mid-rebuild. Cheap to settle by reading the burn design's own
   event ordering once it is written.
3. **The geometric discovery predicate**, only if option 3 is chosen after all: does
   segment-vs-disc pruning actually reach the exact 24% for an r=15 crater, and at what cost
   per candidate pair.

## Harness

Committed at `9e380427` on `wt/shadow-relight`, two throwaway utility commands. Neither
launches the game or writes to disk; both restore any layer they mutate.

```
cd engine
MOD_SEARCH_PATHS="../mods,./mods" ENGINE_DIR=".." dotnet bin/OpenRA.Utility.dll ww3mod \
    --measure-shadow        ../mods/ww3mod/maps/woodland-warfare-ww3
MOD_SEARCH_PATHS="../mods,./mods" ENGINE_DIR=".." dotnet bin/OpenRA.Utility.dll ww3mod \
    --measure-shadow-crater ../mods/ww3mod/maps/woodland-warfare-ww3
```

The `../` on the map path is required — `Platform.EngineDir` resolves to `engine/`, so the
path is read from there, the same trap `CLAUDE.md` records for `--check-yaml`.

`Map.RecomputeShadowFrom` was made public purely so the harness can time it. **Do not merge
this branch.**

## CONFIRMED vs HYPOTHESIS

**CONFIRMED (measured or read directly):** per-call cost and full-bake cost on two maps;
the 162×/163× single-cell waste factor; its collapse to 4.1× at crater scale; byte-exact
subtractability of both terms over all 58,300 crossing pairs; every authored density is a
multiple of 5; the shadow delta per tree (density/5); nothing mutates the layer at runtime;
`ShadowCache.TrySave` is unreachable after load; no renderer reads `ShadowLayer`; the tick is
60 ms; the `ShadowUpdateBudgetPerTick` comment is wrong by ~30× and carries the 1.5× tick-rate
error; the parallel bake's determinism rests on disjoint slot ranges, not ordering.

**HYPOTHESIS (would need the measurement named):**
- *Option 2 is safe on low-core clients.* Confirm with the capped-parallelism run in "what to
  measure next" item 1. This is the one that could still kill the recommendation.
- *A geometric predicate makes per-ray discovery cheap.* Unmeasured; item 3.
- *`HashSet<CPos>` enumeration is reproducible across clients today.* Argued from insertion
  order and pure-integer hash codes, not tested; it should be replaced with an explicit sort
  regardless, so confirming it is not worth the run.
- *A burnt trunk should keep some density rather than none.* A design call, not a measurement —
  but whatever value is chosen must be a multiple of 5.
