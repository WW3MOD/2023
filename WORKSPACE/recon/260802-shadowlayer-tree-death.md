# Recon — does ShadowLayer/DensityLayer update when a tree dies? (2026-08-02, main @ 9392540c)

Re-verification of the [`260728-shadowlayer-tree-death.md`](260728-shadowlayer-tree-death.md) verdict against current HEAD, closing the PIPELINE item 20 open question. Static-code-analysis only — no build, no game, no tests run. Every claim below re-checked with file:line as of `main @ 9392540c` (branch ahead of origin/main by 54; working tree clean apart from untracked non-code files). The prior doc was written at `main @ 893d9882` (then +34); the tree has advanced 20 commits since, so this pass confirms nothing in the shadow/density lifecycle regressed or changed.

## VERDICT: BAKED — unchanged at 9392540c. Density/shadow are frozen at map load; tree death does NOT update them.

Every concealment/cover consumer reads one of two `Map` layers — `ShadowLayer` (derived) or `DensityLayer` (source) — and **neither mutates after load**. The write sites are exhaustively three: (1) load-from-`shadows.bin`, (2) the `SetDensityLayer`/`SetShadowLayer` fallback at load, (3) the dead `UpdateDensityForBuilding`. The mutation hooks that a dying tree would fire are **commented out**, and the mutators are dead code explicitly disabled `260503` for mid-game lag. A tree can be shelled/burned to a husk and its concealment + damage-cover contribution persists unchanged until the next editor save or `--regen-shadows`.

## Line-by-line re-verification (all confirmed at 9392540c)

| Claim | File:line (now) | Status vs 260728 |
|---|---|---|
| The two runtime layers exist as `Map` properties | `Map.cs:252-253` (`DensityLayer`, `ShadowLayer`) | unchanged |
| Load path: `shadows.bin` read verbatim if present | `Map.cs:469-495` | unchanged |
| Fallback compute only if a layer is null after `PostInit` | `Map.cs:501-509` | unchanged |
| `SetDensityLayer` iterates **`ActorDefinitions`** (static map.yaml), not live `World` actors | `Map.cs:977-1003` (loop at `:984`, sum at `:999`) | unchanged |
| `SetShadowLayer` → `RecomputeShadowFrom` per from-cell | `Map.cs:1005-1011` | unchanged |
| `Building.AddedToWorld` density/shadow block is **commented out** | `Building.cs:378-383` | unchanged |
| `Building.RemovedFromWorld` density/shadow block is **commented out** (the hook a dying tree fires — inert) | `Building.cs:392-397` | unchanged |
| Disabled-260503 comment ("computed once at map load and stay frozen … too expensive mid-game") | `Building.cs:372-377` | unchanged |
| `UpdateShadowForCells` flagged `CURRENTLY UNUSED (260503)` | `Map.cs:1013-1032` | unchanged |
| `QueueShadowUpdate` unused | `Map.cs:1034-1043` | unchanged |
| `FlushPendingShadowUpdates` unused; its `World.Tick` caller commented out | `Map.cs:1045-1082`; caller `World.cs:508` (comment `:503-508`) | unchanged |
| `UpdateDensityForBuilding` unused (pure int add/sub with clamp) | `Map.cs:1183-1203` (writes at `:1198/:1200`) | unchanged |

**Write-site grep is exhaustive.** A grep of every assignment to `DensityLayer`/`ShadowLayer` across `engine/` returns only: `Map.cs:473-474` + `:479` + `:491` (load-from-bin), `Map.cs:979` + `:999` (`SetDensityLayer`), `Map.cs:1007` (`SetShadowLayer`), and `Map.cs:1198/:1200` (dead `UpdateDensityForBuilding`). **No sim tick, trait, `INotifyKilled`, `INotifyRemovedFromWorld`, or event writes them.** All other matches are readers or null-guards.

**Caller grep is exhaustive.** The only references to `UpdateDensityForBuilding` / `QueueShadowUpdate` / `UpdateShadowForCells` / `FlushPendingShadowUpdates` outside their own definitions are the **four commented-out lines** in `Building.cs:381-382/:395-396` and the **commented-out** flush at `World.cs:508`. Zero live callers.

## Why tree death can't touch the layers

- Trees are `Building`-derived actors; `BuildingInfo` implements `IDensityInfo` (density source). On death they are removed from the world (husk via `SpawnActorOnDeath`), which fires `INotifyRemovedFromWorld.RemovedFromWorld` → `Building.RemovedFromWorld` (`Building.cs:386-398`). That method removes map/influence registration but its density-decrement + shadow-requeue block is commented (`:392-397`). So the one lifecycle hook wired to a dying tree is inert w.r.t. density/shadow.
- Even a *fresh* fallback compute (`SetDensityLayer`, `Map.cs:984`) reads `ActorDefinitions` — the map file's authored actor list — never live `World` actors. So there is no code path, live or fallback, by which battlefield state (a destroyed tree) reaches the density field mid-match.

## Per-consumer confirmation (all read the frozen layers)

| Consumer | Reads | Evidence (now) |
|---|---|---|
| Vision / Detectable (shadow-attenuated sight) | `ShadowLayer[selfLocation][puv]` | `MapLayers.cs:365` — subtracts baked `groundShadow`/`airborneShadow` from vision strength (`:371-374`) |
| item-26 damage cover | `DensityLayer[c]` windowed per damage event | `DensityModifiesDamage.cs:72-87` — re-reads the byte live, but the byte is frozen ⇒ same value every hit |
| item-21 concealment slot scoring | `DensityLayer[cell]` windowed → `ForestGroundShadow` | `CohesionMoveModifier.cs:301` (`SafeDensity`/`ConcealmentScore`) |
| FiringLOS (can-I-shoot-through) | `ShadowLayer` | `FiringLOS.cs:56,130` |
| Per-unit LOS (auto-target / attack) | `ShadowLayer` | `AutoTarget.cs`, `AttackBase.cs` |
| Weapon `MaxShadowToFire` gate | `ShadowLayer` value | `WeaponInfo.cs` |
| TerrainAffordanceLayer (cover field, no consumer yet) | `DensityLayer` | `TerrainAffordanceLayer.cs:75,83` — computed once at `WorldLoaded` |
| Test hook `GetDensity` | `DensityLayer` | `TestGlobal.cs:267` (read-only) |

No influence-stack field reads `DensityLayer`/`ShadowLayer` directly; the stack touches concealment only transitively through the shadow-reduced vision field (`MapLayers`), itself baked.

## Regen is editor/utility-only (confirmed)

Both regen paths rebuild from `ActorDefinitions` (authored placement), never live actors, and neither runs during a match: in-game **editor save** (`Map.Save` → `SaveShadowsBinaryData`) and the offline **`--regen-shadows`** utility (`RegenShadowsCommand.cs`). There is no sim-tick regen.

## Consequence for case-01 (forest ambush) — unchanged

**Shelling or burning the treeline opens NO sightline and removes NO cover.** Defenders hiding in a forest stay exactly as concealed (vision, `MapLayers.cs:365`), keep full item-26 damage reduction (`DensityModifiesDamage.cs:83`), and keep item-21 concealment scoring (`CohesionMoveModifier.cs`) even after every tree in the sightline is a husk — the driving density is frozen from map load. "Deforestation" is purely cosmetic (husk sprite changes; the field does not). Any case-01 tuning must treat the treeline as **permanent cover**, and any test expecting "shell the trees → they light up" will silently pass on stale data.

Husk-density design input (unchanged): the husk is a separate `SpawnActorOnDeath` actor. Whether a future live system decrements to the husk's own `Building.Density` or to zero is a YAML lookup, not a runtime question — a design input, not a blocker.

## Feasibility sketch — incremental update if the layer is ever made dynamic

The source (`DensityLayer`, per-cell `byte`) and the derived (`ShadowLayer`, per cell-**pair** attenuation) have very different recompute costs, so a live update splits into two tiers:

**Cheap tier — damage-cover + concealment-scoring go live for near-zero cost.** `DensityModifiesDamage`, `CohesionMoveModifier.ConcealmentScore`, and `TerrainAffordanceLayer` read `DensityLayer` **directly** and per-access. Re-enabling *only* the density decrement in `Building.RemovedFromWorld` (`Building.cs:392-397` → `UpdateDensityForBuilding(self.Location, Info.Density, add:false)`, `Map.cs:1187-1203`) makes the damage + concealment-scoring consumers reflect tree death **immediately, with zero shadow recompute** — they re-read the byte each access. (`TerrainAffordanceLayer` is computed once at `WorldLoaded` and would need an explicit recompute call, but it has no consumer yet.) This is the minimal, per-cell, locally-patchable seam and it fixes the case-01 *damage* wart alone. Cost per tree death: O(footprint cells) integer writes — the density field IS per-cell and locally patchable.

**Expensive tier — vision/FiringLOS need the derived `ShadowLayer` rebuilt.** The sight paths read the precomputed per-pair `ShadowLayer`, so opening a *sightline* requires re-running `RecomputeShadowFrom` over affected from-cells — a global-ish bake, not a local patch. Options, cheapest last:
- *Full rebuild* (`SetShadowLayer`, `Map.cs:1005-1011`): every from-cell × its 2–32 annulus ≈ millions of line traces — the original 260503 lag source. Unacceptable per death.
- *Annulus rebuild* (`UpdateShadowForCells`, `Map.cs:1020-1032`): recompute from-cells within 0–32 of the dead cell ≈ π·32² ≈ 3.2k from-cells, each retracing its ~3.2k annulus ≈ ~10M traces per death — still the disabled-for-lag path. `FlushPendingShadowUpdates` (`Map.cs:1050-1082`) already sketches a budgeted/deferred amortization over ticks (`ShadowUpdateBudgetPerTick`).
- *Local-window rebuild (new, cheapest, not written)*: recompute only the (from,to) **pairs whose sightline actually crosses the dead cell** — a small fraction of the annulus. Needs a "which pairs cross cell C" index or an on-the-fly `TilesIntersectingLine` membership test.

**Byte-identity / zero-RNG implications** (density feeds sim-visible `IDamageModifier`, so this is lockstep-critical; see [`influence-stack.md`](../../DOCS/reference/influence-stack.md) invariants):
- The arithmetic is safe: `UpdateDensityForBuilding` is pure integer add/sub with clamp; `ForestGroundShadow` and the ground shadow path are integer. No new RNG.
- The real risk is **baseline divergence**, not the update math. Live decrements sit on top of a per-client baseline that today comes from `shadows.bin`; if one client loads a stale bin while another regenerated, deterministic decrements *preserve* (not cure) the mismatch. A live design must guarantee all clients start from the same baseline (same bin, or all-fallback-compute) and apply the same sim-ordered tree deaths. Tree removal already fires in the deterministic sim (`RemovedFromWorld`), so ordering is synchronized. The one spot to audit if the shadow rebuild is re-enabled is the **airborne** shadow term (`Ceiling(Σ density/5)`, a float) for cross-platform byte-identity; the ground path is integer and fine.

## UNVERIFIED-NEEDS-RUNTIME

None. The BAKED verdict is fully settled by static reading: the mutation hooks are commented out, the mutators have zero live callers, and the only fallback compute reads authored `ActorDefinitions`. This is dispositive without running anything. The husk-density point above is a YAML lookup (design input), not a runtime question, and does not affect the verdict.
