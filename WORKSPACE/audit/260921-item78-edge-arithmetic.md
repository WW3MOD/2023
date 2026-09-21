# Item 78 — the evacuation edge arithmetic, and the item's real status

**Base: `wt/item78-study`, branched from `main @ 70e63582`, 2026-09-21. Static only: no build, no
launch, no lint, no YAML touched under `mods/`.** Every `file:line` below was opened at that ref.
Numbers were re-derived here by running `tools/evac-edge-math/`, not relayed.

---

## Verdict, before the arithmetic

**Neither DROP nor KEEP. Item 78 is CLOSED — it was measured on 2026-09-05 and the fix SHIPPED the
same day at `ac16f2b6`, which is an ancestor of this base.** The study that audit package 14 asks
for was already performed, by the tool it asks me to write, and the item's proposed one-token change
is the code that runs today.

```
$ git log --oneline -S FriendlyEvacuationOrigin -- engine/OpenRA.Mods.Common/Activities/RotateToEdge.cs
ac16f2b6 Item 78: ground evacuation exits through the owner's own side, not the nearest wall
$ git merge-base --is-ancestor ac16f2b6 HEAD   # -> ancestor of HEAD
```

**The premise held, so DROP would have been the wrong answer had the item still been open.** Before
the fix, a unit raiding an opponent's spawn left through an opponent's wall **70.4%** of the time and
through its own **14.4%**. The dossier's fear — that the nearest edge is usually the owner's own —
is refuted by a factor of five.

### Why the audit read it as open

Audit package 14 says *"STILL OPEN … No evacuation-edge study exists in `WORKSPACE/`."* That
sentence is **literally true and materially wrong**: the study is at `tools/evac-edge-math/`
(tool + README) with its results written into the dossier's own status log, not under `WORKSPACE/`.
A `WORKSPACE`-scoped search cannot see it. This is the pipeline README's *"a merged branch is not a
finished item"* trap running in reverse — a finished item read as unstarted because the evidence was
filed somewhere the grep did not reach.

---

## The brief's three clauses, re-derived from source

The brief instructed me not to trust the dossier's paraphrase. Each clause, checked against
`engine/OpenRA.Mods.Common/Activities/RotateToEdge.cs` at `70e63582`:

| Clause as written in the stub | Verdict at this ref |
|---|---|
| "aircraft use `HomeLocation`" | **CONFIRMED.** `:195` — `var searchOrigin = spawnAreaHint ?? self.Owner.HomeLocation;` Untouched by the fix, deliberately. |
| "ground uses `self.Location`" | **FALSE NOW, true before `ac16f2b6`.** `:209` reads `spawnAreaHintGround ?? FriendlyEvacuationOrigin(self)`. The stub's line cite `:165-166` has drifted to `:209`. |
| "nine of ten maps have no `spawnarea`" | **CONFIRMED.** `grep -ci spawnarea` over each `mods/ww3mod/maps/*/map.yaml`: `river-zeta-ww3` = 6, every other map = 0. |

`FriendlyEvacuationOrigin` (`:158-170`) returns the nearest friendly `SUPPLYROUTE`
(`ProductionFromMapEdge` filtered by `self.Owner.IsAlliedWith`, ranked by distance to the unit),
then `self.Owner.HomeLocation`, then `self.Location` — the last arm guarded against `CPos.Zero`
because a map player with no lobby slot defaults there and would otherwise evacuate to the map's
top-left corner.

The chooser the ground branch calls is `Map.ChooseClosestMatchingEdgeCell`
(`engine/OpenRA.Game/Map/Map.cs:1891`), an exact filtered argmin over the perimeter —
**not** `ChooseClosestEdgeCell` (`:1833`), the half-plane projection the aircraft branch uses.

---

## Definitions used

- **Own / enemy / flank wall.** Each of the four `Bounds` walls is attributed to the spawn that sits
  nearest it. The exit cell's wall is then "own" if it is the wall nearest the evacuating unit's
  owner's spawn, "enemy" if it is nearest an opponent's, "flank" if no spawn claims it.
- **Ambiguity, and it is the whole of the residual.** On `twin-rivers-ww3` and `x-lake-ww3` two
  spawns share the Left wall (and two the Right), so a unit's "own wall" is simultaneously an
  opponent's own wall. Those two maps contribute **the entire** 14.4% pre-fix own-wall figure; on the
  six two-spawn maps and on `seventh-woods-ww3` the pre-fix raid own-wall rate is **0.0%**.
- **Populations.** `all` = every passable cell; `enemy-half` = cells closer to an opponent's spawn
  than to the owner's; `raid` = cells within 20 cells of an opponent's spawn. **`raid` is the
  population the item's premise is about** and is the one quoted below.
- **Passability is real, not a grid approximation.** The tool decodes `map.bin` through nav-guard's
  decoder and applies the `tracked` locomotor plus a connected-component reachability test, which is
  what the engine's `CanEnterCell && CanReach` predicate does.
- **`river-zeta-ww3` is a control, not a result.** It is the one map authoring `spawnarea`, so
  `FindClosestSpawnAreaForOwner` returns non-null and item 78 changed nothing there. Its before and
  after rows are identical by construction. **Quoting a ten-map aggregate mixes the control into the
  treatment** — every headline number below is the nine-map figure.

---

## The numbers

Nine unit-anchored maps, `raid` population, **n = 7170** owner/cell pairs (river-zeta's 4797
excluded; 7170 + 4797 = 11967, the tool's own total, so the split is exact with no residual).
Per-map rows weighted by each map's sample size:

| regime | own wall | opponent's wall | neutral flank | median drive |
|---|---|---|---|---|
| **before** (`?? self.Location`, shipped to 2026-09-05) | **14.4%** | **70.4%** | 15.3% | ~9 cells |
| **after** (`?? FriendlyEvacuationOrigin`, ships now) | **99.5%** | **0.4%** | 0.1% | ~108 cells |

Per map, `raid`, opponent's-wall share **before** the fix:

| map | spawns | opp. wall before | own wall before | own wall after |
|---|---|---|---|---|
| seventh-woods-ww3 | 4 | **100.0%** | 0.0% | 99.2% |
| shellmap-open-field | 2 | 88.0% | 0.0% | 100.0% |
| siberian-pass-ww3 | 2 | 83.4% | 0.0% | 99.2% |
| polar-disorder-ww3 | 2 | 70.3% | 0.0% | 99.2% |
| woodland-warfare-ww3 | 2 | 67.0% | 0.0% | 99.1% |
| nuclear-winter-ww3 | 2 | 58.9% | 0.0% | 100.0% |
| twin-rivers-ww3 | 4 | 57.8% | 28.9% | 99.6% |
| x-lake-ww3 | 4 | 55.1% | 27.6% | 99.6% |
| arena-tank-duel | 2 | 31.8% | 0.0% | 100.0% |
| _river-zeta-ww3 (control)_ | _6_ | _0.0%_ | _100.0%_ | _100.0%_ |

**The free-raid premise held on all nine.** The weakest map is `arena-tank-duel` at 31.8%, and it is
a 64×32 duel map where nothing is far from anything; the median map is around 67%.

### Cost of the fix — the extra walk

Median drive to the exit, `raid` population, before → after:

| map | before | after | ×  |
|---|---|---|---|
| arena-tank-duel | 6.0 | 52.4 | 8.7 |
| nuclear-winter-ww3 | 5.0 | 104.2 | 20.8 |
| polar-disorder-ww3 | 7.0 | 107.0 | 15.3 |
| seventh-woods-ww3 | 8.0 | 111.9 | 14.0 |
| shellmap-open-field | 10.0 | 79.4 | 7.9 |
| siberian-pass-ww3 | 8.2 | 91.4 | 11.1 |
| twin-rivers-ww3 | 11.0 | 113.4 | 10.3 |
| woodland-warfare-ww3 | 9.3 | 118.1 | 12.7 |
| x-lake-ww3 | 9.2 | 118.0 | 12.8 |

**Worst case** (the dossier's own suggested launch test, answered on paper): `twin-rivers-ww3`, owner
spawn `1,22`, unit in the far corner `124,124` — shipped-before exit `124,126` (Bottom, **2.0
cells**), shipped-after exit `1,22` (Left, **159.8 cells**). An **80×** worst case against a ~12×
median.

**Reproduction of the dossier's headline.** Its status log claims 14.4 / 70.4 / 15.3. Weighting the
nine per-map rows by sample size gives **14.36 / 70.37 / 15.27**. That is an exact reproduction to
the printed precision, at a different ref, by a different person. The 99.5% / 0.4% after-figures
reproduce likewise.

**One number I did not reproduce exactly.** The medians. The tool prints a median per map, and
medians do not recombine into a pooled median by weighting. The dossier's pooled `9.0 → 108.1` is
consistent with the per-map spread above and I have no reason to doubt it, but I bounded it rather
than re-derived it, and it is the one figure here I am relaying.

---

## Recommendation

**Close item 78 as shipped.** The arithmetic the stub demands as a precondition was done on
2026-09-05, it settled the premise in favour of acting, and the action was taken and merged. The
stub's own instruction — *"if it says the nearest edge is usually the owner's own, DROP this item"* —
resolves to **do not drop**: the nearest edge was an opponent's on seven of nine maps at better than
55%, and on `seventh-woods-ww3` at 100%.

Per [`WORKSPACE/pipeline/README.md`](../pipeline/README.md) §"Closing an item" this pass moves the
dossier verbatim into `archive/closed-items.md`, deletes the stub from `PIPELINE.md`, adds a row to
the load-bearing table and a line to `archive/shipped-log.md`.

**One residual is carried rather than closed**, and it is a user decision, not a work item: the fix
multiplies evacuation exposure ~12× (median 9 → 108 cells) and the user approved *the rule*, not
that consequence. `INotifySold.Sold` fires only on arrival (`RotateToEdge.cs:517`) and the refund
scales with current HP (`:511`), so a unit killed en route banks **zero**. That has been moved to
`AWAITING-USER.md` so closing the item does not destroy the only pointer to it.

---

## What this study could NOT settle statically

- **Path length, not straight-line distance.** Every "drive" figure is Euclidean cell distance
  between the unit and its chosen exit. The actual drive is a pathfinder route around water, cliffs
  and buildings, so every number is a **lower bound** — and it is a lower bound that is looser for
  the 108-cell cross-map trip than for the 9-cell one, which means the true post-fix exposure
  multiplier is **≥ 12×**, not ≈ 12×.
- **Whether 9 hostile cells cost more than 108 mixed ones.** The short pre-fix trip runs through the
  enemy's most defended ground; the long post-fix trip is mostly through open or friendly ground.
  Geometry cannot price that, and it is the one real argument against the change. This is the
  dossier's own caveat and it survives this pass intact.
- **Blocked or contested exit cells at runtime.** The tool applies static passability and static
  connectivity. It cannot model a perimeter cell transiently blocked by another actor, nor the
  unit-anchored retry at `RotateToEdge.cs:362` firing when a queued evacuation's committed
  destination goes unreachable mid-drive.
- **Bot behaviour.** `RotateToEdge` is shared by five callers and both bot profiles. Nothing here
  measures how often bots evacuate at all, so the change's aggregate effect on a match is unmeasured.

**What one launch would settle**, if the manager decides to spend it: run
`tools/autotest/scenarios/test-evac-exits-own-side/` and read the **realised path length and
survival** of a unit evacuating from deep in enemy territory — the one quantity this study
structurally cannot produce. The pre-existing scenario already grades the core rule with an enemy
Supply Route as the negative control. It does **not** cover the allied-SR clause, which needs a third
player and a `PlayerReference` alliance and has no scenario anywhere in the tree.
