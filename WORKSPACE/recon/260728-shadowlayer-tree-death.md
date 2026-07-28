# Recon — does ShadowLayer/DensityLayer update when trees die? (2026-07-28, main @ 893d9882)

Static-code-analysis-only follow-up to [`260728-trees-concealment.md`](260728-trees-concealment.md) (Open Question, PIPELINE item 20). No build, no game, no tests run — every claim is code-verified with file:line as of main @ 893d9882 (branch ahead of origin/main by 34, working tree clean apart from this doc). Gates the "Dynamic battlefield foliage" backlog idea and case-01 (forest ambush) correctness.

## VERDICT: BAKED — density/shadow are frozen at map load; tree death does NOT update them.

Every concealment/cover consumer reads one of two layers — `Map.ShadowLayer` (derived) or `Map.DensityLayer` (source) — and **neither mutates at runtime**. The only methods that could mutate them (`UpdateDensityForBuilding`, `QueueShadowUpdate`, `UpdateShadowForCells`, `FlushPendingShadowUpdates`) are all dead code, explicitly disabled `260503` for mid-game lag, and the `Building.RemovedFromWorld` hook that would drive them is **commented out**. A tree can be shelled/burned to a husk and its concealment + damage-cover contribution persists unchanged until the next editor save or `--regen-shadows`.

## ShadowLayer / DensityLayer lifecycle

Two runtime fields (`Map.cs:252-253`):
- `DensityLayer : CellLayer<byte>` — per-cell summed tree `Building.Density` (the **source** field).
- `ShadowLayer : CellLayer<CellLayer<(byte GroundShadow, byte AirborneShadow)>>` — per cell-pair precomputed sightline attenuation (**derived** from DensityLayer via the `ForestGroundShadow` curve).

**Load** (`Map.cs:469-509`):
- If `shadows.bin` is present in the package → both layers are read verbatim from disk (`Map.cs:469-493`). This is the normal path for shipped maps.
- Fallback if absent → after `PostInit`, `SetDensityLayer()` + `SetShadowLayer()` compute fresh (`Map.cs:505-509`).

**Density build** (`SetDensityLayer`, `Map.cs:977-1003`): iterates **`ActorDefinitions`** — the map file's *static authored actor list* (map.yaml), **not** live `World` actors — resolves each `IDensityInfo.Density()` and sums into `DensityLayer`. So even a fresh compute reflects *authored* tree placement, never battlefield state.

**Shadow build** (`SetShadowLayer` → `RecomputeShadowFrom`, `Map.cs:1005-1010, 1126-1181`): for every from-cell, traces the line to each to-cell in annulus 2-32, sums crossed `DensityLayer` (excluding endpoints, `Map.cs:1154`), runs the sum through `ForestGroundShadow` (ground) / `Ceiling(Σ density/5)` (airborne), stores the byte pair. Pure integer on the ground path (float only for airborne ceil).

**Write sites after load — exhaustive:** there are none in the sim path. `DensityLayer`/`ShadowLayer` are assigned only by (a) load-from-bin, (b) the `SetDensityLayer`/`SetShadowLayer` fallback at load, and (c) `SaveShadowsBinaryData` (`Map.cs:947-975`, editor/utility only — see below). No sim tick, trait, or event writes them.

## Nothing subscribes to tree death to touch density/shadow

- Trees are `Building`-derived actors; `BuildingInfo` implements `IDensityInfo` (`Building.cs:29,141`).
- `Building` has the actor-lifecycle hooks, and the density-mutation calls sit **inside them, commented out**:
  - `AddedToWorld` (`Building.cs:359-384`): the `UpdateDensityForBuilding(..., add:true)` + `QueueShadowUpdate(...)` block is commented (`Building.cs:378-383`).
  - `RemovedFromWorld` (`Building.cs:386-398`): the `UpdateDensityForBuilding(..., add:false)` + `QueueShadowUpdate(...)` block is commented (`Building.cs:392-397`). This is the hook a dying tree fires; it is inert.
  - Comment (`Building.cs:372-377`): *"Dynamic shadow recalc disabled 260503 … Shadows are computed once at map load and stay frozen. The recalc was too expensive to run mid-game (visible lag on building destruction)."*
- The mutators themselves are all flagged `CURRENTLY UNUSED (260503)`: `UpdateShadowForCells` (`Map.cs:1013-1032`), `QueueShadowUpdate` (`Map.cs:1034-1043`), `FlushPendingShadowUpdates` (`Map.cs:1045-1082`, its `World.Tick` caller commented out), `UpdateDensityForBuilding` (`Map.cs:1183-1203`).
- No `INotifyKilled`/`ActorRemoved`/`RemoveFromWorld` handler anywhere recomputes or decrements density or shadow. Grep of every `DensityLayer`/`ShadowLayer` reader (below) confirms all are read-only consumers.

## Per-consumer evidence (all BAKED)

| Consumer | Reads | Live or baked | Evidence |
|---|---|---|---|
| Vision / Detectable (shadow-attenuated sight) | `ShadowLayer[selfLocation][puv]` | **BAKED** | `MapLayers.cs:360-368` — subtracts baked `groundShadow`/`airborneShadow` from vision strength; ShadowLayer never recomputed at runtime |
| item-26 damage cover | `DensityLayer[c]` windowed | **BAKED** | `DensityModifiesDamage.cs:71-87` — reads DensityLayer live *per damage event*, but DensityLayer itself is frozen ⇒ same value every read |
| item-21 concealment slot scoring | `DensityLayer[cell]` windowed → `ForestGroundShadow` | **BAKED** | `CohesionMoveModifier.cs:299-346` (`SafeDensity`/`ConcealmentScore`) — frozen DensityLayer snapshot |
| FiringLOS (can-I-shoot-through) | `ShadowLayer[lookupFrom]` | **BAKED** | `FiringLOS.cs:56,99,151` |
| Per-unit LOS (auto-target / attack) | `ShadowLayer` | **BAKED** | `AutoTarget.cs:1109`, `AttackBase.cs:249` |
| Weapon `MaxShadowToFire` gate | `ShadowLayer` value | **BAKED** | `WeaponInfo.cs:143` |
| TerrainAffordanceLayer (cover-quality field) | `DensityLayer` | **BAKED** (computed once at `WorldLoaded`, `TerrainAffordanceLayer.cs:65-78`; "no consumer yet") |
| Test hook `GetDensity` | `DensityLayer` | read-only (`TestGlobal.cs:259-270`) |

**Territory/danger overlays:** no influence-stack field reads `DensityLayer`/`ShadowLayer` directly — the grep of all readers returns only the rows above. The influence stack touches concealment only *transitively*, through the shadow-reduced vision field (`MapLayers`), which is itself baked. So no additional live path exists there.

**Ground-shadow curve & `ForestGroundShadow` (item 26):** `ForestGroundShadow` (`Map.cs:1102-1120`) is a pure static consumed in two places — `RecomputeShadowFrom` (bake time, `Map.cs:1176`) and `ConcealmentScore` (order time, `CohesionMoveModifier.cs:345`). Both feed it a **DensityLayer** sum, and DensityLayer is frozen, so the superlinear curve operates on stale input after a tree dies. The in-code PITFALL at `Map.cs:1172-1174` already says the ground curve is *"BAKED into shadows.bin at map load … A stale cache silently keeps the old concealment."* — the same staleness now also applies to the item-26 **damage-cover** path, since `DensityModifiesDamage` reads that same frozen DensityLayer.

## Regen is editor/utility-only (confirmed)

Both regen paths call `SaveShadowsBinaryData()` (`Map.cs:947-975`), which runs `SetDensityLayer()` + `SetShadowLayer()` over **`ActorDefinitions`** (static map.yaml), never live actors:
- **In-game editor save** — `Map.Save` writes `shadows.bin` alongside `map.yaml`/`map.bin` (`Map.cs:881`). Only fires on an explicit editor save, and rebuilds from authored placement.
- **`--regen-shadows` utility** — `RegenShadowsCommand.cs:26-36`, offline, updates only `shadows.bin`.

Neither runs during a match. There is no sim-tick regen. Confirmed: regen is editor/utility-only, and even then reflects authored trees, not mid-battle deaths.

## Most important consequence for case-01 (forest ambush)

**Shelling or burning the treeline opens NO sightline and removes NO cover.** If attackers artillery/flame the forest to husks, the defenders hiding in it stay exactly as concealed (vision path, `MapLayers.cs:362`) *and* keep the full item-26 damage reduction (`DensityModifiesDamage.cs:83`) and item-21 concealment scoring (`CohesionMoveModifier.cs:343`) of a fully-standing forest — the density that drives all three is frozen from map load. This is the correctness wart the Open Question feared: "deforestation" is purely cosmetic (the husk sprite changes; the concealment/cover field does not). Any case-01 tuning must assume the treeline is *permanent cover*, and any test that expects "shell the trees → they light up" will silently pass on stale data.

Caveat worth noting for a future fix: on tree death the husk is a *separate* actor (`SpawnActorOnDeath`); whether the husk carries `Building.Density` decides whether a live system should *decrement to the husk's* value or to zero. Static reading can't settle the husk's own density here without chasing the husk actor's YAML — flag as a design input, not a blocker (the current answer is BAKED regardless).

## Live-update seam sketch (NOT implemented)

Two tiers, because the source layer (DensityLayer) and the derived layer (ShadowLayer) have very different recompute costs.

**Cheap tier — damage-cover + concealment-scoring go live for near-zero cost.** `DensityModifiesDamage`, `CohesionMoveModifier.ConcealmentScore`, and `TerrainAffordanceLayer` read **DensityLayer directly**. Re-enabling just the density decrement in `Building.RemovedFromWorld` (`Building.cs:392-397` → `UpdateDensityForBuilding(self.Location, Info.Density, add:false)`, `Map.cs:1187-1203`) makes those two consumers reflect tree death *immediately, with zero shadow recompute* (they re-read the byte each access; `TerrainAffordanceLayer` is computed once and would need an explicit recompute call, but has no consumer yet). This is the minimal seam that fixes the case-01 *damage* wart.

**Expensive tier — vision/FiringLOS need the derived ShadowLayer rebuilt.** The sight paths (`MapLayers.cs:362`, `FiringLOS`, `AutoTarget`, `AttackBase`, weapon gate) read the precomputed `ShadowLayer`, so opening a *sightline* needs `RecomputeShadowFrom` over affected from-cells. Cost options:
- *Full rebuild* (`SetShadowLayer`, `Map.cs:1005-1010`): every from-cell × its 2-32 annulus ≈ millions of line traces — the original 260503 lag source; unacceptable per death.
- *Annulus rebuild* (`UpdateShadowForCells`, `Map.cs:1020-1032`): recompute only from-cells within 0-32 of the dead cell ≈ π·32² ≈ 3.2k from-cells, each retracing its ~3.2k-cell annulus ≈ ~10M traces per tree death — still the disabled-for-lag path. `FlushPendingShadowUpdates` (`Map.cs:1050-1082`) already sketches a budgeted/deferred version to amortize this over ticks.
- *Local-window rebuild (new, cheapest)*: only recompute the (from,to) **pairs whose sightline actually crosses the dead cell** — a small fraction of the annulus. Not yet written; would need a "which pairs cross cell C" index or an on-the-fly `TilesIntersectingLine` membership test.

**Zero-RNG / byte-identity implications** ([`influence-stack.md`](../../DOCS/reference/influence-stack.md) invariants — density feeds sim-visible `IDamageModifier`, so this is a lockstep-critical layer):
- The arithmetic is safe: `UpdateDensityForBuilding` is pure integer add/sub with clamp; `ForestGroundShadow` is pure integer; the ground shadow path is integer. No new RNG.
- The real risk is **baseline divergence**, not the update math. Live decrements layer on top of a per-client baseline that today comes from `shadows.bin` — if any client loads a *stale* bin while another regenerated, they start from different frozen fields and the deterministic decrements preserve (not cure) the mismatch. A live design must guarantee all clients start from the *same* baseline (same bin, or all-fallback-compute) and apply the same sim-ordered tree deaths. Tree removal already fires in the deterministic sim (`RemovedFromWorld`), so ordering is synchronized; the airborne `Ceiling(float)` term is the one spot to audit for cross-platform byte-identity if the shadow rebuild is re-enabled (ground path is integer and fine).

## UNVERIFIED-NEEDS-RUNTIME

None. The staleness verdict is fully settled by static reading — the mutation hooks are commented out and the mutators are dead code, which is dispositive without running anything. (The husk-density design input above is a YAML lookup, not a runtime question, and does not affect the BAKED verdict.)
